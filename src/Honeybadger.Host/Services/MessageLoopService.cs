using System.Diagnostics;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data.Entities;
using Honeybadger.Data.Repositories;
using Honeybadger.Host.Agents;
using Honeybadger.Host.Formatting;
using Honeybadger.Host.Memory;
using Honeybadger.Host.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using AIFunction = Microsoft.Extensions.AI.AIFunction;

namespace Honeybadger.Host.Services;

/// <summary>
/// Reads incoming chat messages from IChatFrontend, routes through GroupQueue,
/// invokes IAgentRunner, and delivers responses back to the user.
/// Supports multi-agent routing via AgentRegistry.
/// </summary>
public class MessageLoopService(
    IChatFrontend frontend,
    IAgentRunner agentRunner,
    GroupQueue groupQueue,
    HierarchicalMemoryStore memoryStore,
    IServiceScopeFactory scopeFactory,
    IOptions<HoneybadgerOptions> options,
    AgentRegistry agentRegistry,
    AgentToolFactory agentToolFactory,
    ILogger<MessageLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MessageLoopService started");

        await foreach (var message in frontend.IncomingMessages.ReadAllAsync(stoppingToken))
        {
            var msg = message; // capture for closure
            await groupQueue.EnqueueAsync(msg.GroupName, ct => ProcessMessageAsync(msg, ct), stoppingToken);
        }
    }

    /// <summary>
    /// Determine which agent configuration to use for this message.
    /// Returns the router agent if configured, otherwise null for legacy mode.
    /// </summary>
    private AgentConfiguration? DetermineAgentForMessage(string groupName)
    {
        var routerAgent = agentRegistry.GetRouterAgent();
        if (routerAgent is not null)
        {
            logger.LogDebug("Multi-agent mode active: using router agent '{AgentId}'", routerAgent.AgentId);
            return routerAgent;
        }

        logger.LogDebug("Legacy mode: no router agent configured");
        return null;
    }

    private async Task ProcessMessageAsync(ChatMessage message, CancellationToken ct)
    {
        // Generate CorrelationId for this message's journey through the system
        var correlationId = Guid.NewGuid().ToString("N")[..12];

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        using var __ = LogContext.PushProperty("GroupName", message.GroupName);
        using var ___ = LogContext.PushProperty("MessageId", message.Id);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            logger.LogDebug("Processing message from {Sender} ({ContentLength} chars)",
                message.Sender, message.Content.Length);

            await frontend.ShowAgentThinkingAsync(message.GroupName, ct);

            using var scope = scopeFactory.CreateScope();
            var msgRepo = scope.ServiceProvider.GetRequiredService<MessageRepository>();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<SessionRepository>();

            // Load recent conversation history for context (BEFORE persisting current message)
            var opts = options.Value;
            var recentMessages = await msgRepo.GetRecentMessagesAsync(message.GroupName, opts.Agent.ConversationHistoryCount, ct);
            var conversationHistory = ConversationFormatter.Format(recentMessages, opts.Agent.ConversationHistoryTokenBudget);
            logger.LogDebug("Loaded {Count} recent messages", recentMessages.Count);

            // Persist the user's message (AFTER loading history, so it won't be in the context)
            await msgRepo.AddMessageAsync(message.GroupName, message.Id, message.Sender, message.Content, false, ct);
            logger.LogDebug("Persisted user message");

            // Resolve session for conversation continuity
            var session = await sessionRepo.GetLatestAsync(message.GroupName, ct);
            if (session is not null)
                logger.LogDebug("Resolved session: {SessionId}", session.SessionId);
            else
                logger.LogDebug("No existing session");

            // Determine which agent to use (router if configured, null for legacy mode)
            var agentConfig = DetermineAgentForMessage(message.GroupName);

            // Resolve model and CLI endpoint
            var model = agentConfig?.Model ?? opts.GetModelForGroup(message.GroupName);
            var cliEndpoint = opts.Agent.CopilotCli.AutoStart
                ? $"localhost:{opts.Agent.CopilotCli.Port}"
                : string.Empty;

            logger.LogDebug("Resolved model={Model}, cliEndpoint={Endpoint}",
                model, string.IsNullOrEmpty(cliEndpoint) ? "(none)" : cliEndpoint);

            // Build agent request
            var globalMemory = memoryStore.LoadGlobalMemory();

            // For router agents, inject available agents summary into global memory
            if (agentConfig?.IsRouter == true)
            {
                var agentSummary = agentRegistry.GetAgentSummary();
                if (!string.IsNullOrWhiteSpace(agentSummary))
                {
                    globalMemory = string.IsNullOrWhiteSpace(globalMemory)
                        ? $"## Available Specialist Agents\n{agentSummary}"
                        : $"{globalMemory}\n\n## Available Specialist Agents\n{agentSummary}";
                    logger.LogDebug("Injected agent summary into global memory");
                }
            }

            var request = new AgentRequest
            {
                CorrelationId = correlationId,
                MessageId = message.Id,
                GroupName = message.GroupName,
                Content = message.Content,
                SessionId = session?.SessionId,
                Model = model,
                GlobalMemory = globalMemory,
                GroupMemory = memoryStore.LoadGroupMemory(message.GroupName),
                AgentMemory = memoryStore.LoadGroupAgentMemory(message.GroupName),
                ConversationSummary = memoryStore.LoadGroupSummary(message.GroupName),
                ConversationHistory = conversationHistory,
                CopilotCliEndpoint = cliEndpoint,
                // Multi-agent fields
                AgentId = agentConfig?.AgentId,
                IsRouterAgent = agentConfig?.IsRouter ?? false,
                Soul = agentConfig?.Soul is not null ? agentRegistry.LoadSoulFile(agentConfig.Soul) : null,
                AvailableTools = agentConfig?.Tools ?? []
            };

            logger.LogInformation("Invoking agent for group {Group} with model {Model} (AgentId={AgentId})",
                message.GroupName, model, agentConfig?.AgentId ?? "(legacy)");

            // Build tools for the agent
            IEnumerable<AIFunction>? tools = null;
            if (agentConfig is not null)
            {
                tools = agentToolFactory.CreateToolsForAgent(agentConfig, message.GroupName, correlationId);
                logger.LogDebug("Created tools for agent {AgentId}", agentConfig.AgentId);
            }

            // Stream response chunks as they arrive
            var streamedContent = new System.Text.StringBuilder();
            var agentStopwatch = Stopwatch.StartNew();
            var response = await agentRunner.RunAgentAsync(request, async chunk =>
            {
                streamedContent.Append(chunk);
                await frontend.SendStreamChunkAsync(message.GroupName, chunk, ct);
            }, ct, tools);
            agentStopwatch.Stop();

            logger.LogDebug("Agent runner returned Success={Success} ({ContentLength} chars) in {ElapsedMs}ms",
                response.Success, (response.Content ?? response.Error ?? "").Length, agentStopwatch.ElapsedMilliseconds);

            // Signal streaming is complete
            if (streamedContent.Length > 0)
                await frontend.SendStreamCompleteAsync(message.GroupName, ct);

            // Persist updated session
            if (!string.IsNullOrEmpty(response.SessionId))
            {
                await sessionRepo.UpsertAsync(message.GroupName, response.SessionId, ct);
                logger.LogDebug("Session persisted: {SessionId}", response.SessionId);
            }

            // Persist agent response
            var agentContent = response.Success
                ? response.Content ?? string.Empty
                : response.Error ?? "Unknown error";
            await msgRepo.AddMessageAsync(message.GroupName, Guid.NewGuid().ToString(), "agent", agentContent, true, ct);

            await frontend.HideAgentThinkingAsync(message.GroupName, ct);

            // Only send full message if we didn't stream (e.g., errors or no streaming support)
            if (streamedContent.Length == 0)
            {
                await frontend.SendToUserAsync(new ChatMessage
                {
                    GroupName = message.GroupName,
                    Content = response.Success ? agentContent : $"[Error] {agentContent}",
                    Sender = "agent",
                    IsFromAgent = true
                }, ct);
            }

            stopwatch.Stop();
            logger.LogDebug("Message processing complete in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Error processing message for group {Group} after {ElapsedMs}ms",
                message.GroupName, stopwatch.ElapsedMilliseconds);
            await frontend.HideAgentThinkingAsync(message.GroupName, ct);
            await frontend.SendToUserAsync(new ChatMessage
            {
                GroupName = message.GroupName,
                Content = $"[Error] {ex.Message}",
                Sender = "system",
                IsFromAgent = true
            }, ct);
        }
    }
}
