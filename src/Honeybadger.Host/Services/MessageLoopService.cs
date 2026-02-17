using System.Diagnostics;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data.Entities;
using Honeybadger.Data.Repositories;
using Honeybadger.Host.Memory;
using Honeybadger.Host.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Honeybadger.Host.Services;

/// <summary>
/// Reads incoming chat messages from IChatFrontend, routes through GroupQueue,
/// invokes IAgentRunner, and delivers responses back to the user.
/// </summary>
public class MessageLoopService(
    IChatFrontend frontend,
    IAgentRunner agentRunner,
    GroupQueue groupQueue,
    HierarchicalMemoryStore memoryStore,
    IServiceScopeFactory scopeFactory,
    IOptions<HoneybadgerOptions> options,
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

            // Persist the user's message
            await msgRepo.AddMessageAsync(message.GroupName, message.Id, message.Sender, message.Content, false, ct);
            logger.LogDebug("Persisted user message");

            // Load recent conversation history for context
            var opts = options.Value;
            var recentMessages = await msgRepo.GetRecentMessagesAsync(message.GroupName, opts.Agent.ConversationHistoryCount, ct);
            var conversationHistory = FormatConversationHistory(recentMessages);
            logger.LogDebug("Loaded {Count} recent messages", recentMessages.Count);

            // Resolve session for conversation continuity
            var session = await sessionRepo.GetLatestAsync(message.GroupName, ct);
            if (session is not null)
                logger.LogDebug("Resolved session: {SessionId}", session.SessionId);
            else
                logger.LogDebug("No existing session");

            // Resolve model and CLI endpoint
            var model = opts.GetModelForGroup(message.GroupName);
            var cliEndpoint = opts.Agent.CopilotCli.AutoStart
                ? $"localhost:{opts.Agent.CopilotCli.Port}"
                : string.Empty;

            logger.LogDebug("Resolved model={Model}, cliEndpoint={Endpoint}",
                model, string.IsNullOrEmpty(cliEndpoint) ? "(none)" : cliEndpoint);

            var request = new AgentRequest
            {
                CorrelationId = correlationId,
                MessageId = message.Id,
                GroupName = message.GroupName,
                Content = message.Content,
                SessionId = session?.SessionId,
                Model = model,
                GlobalMemory = memoryStore.LoadGlobalMemory(),
                GroupMemory = memoryStore.LoadGroupMemory(message.GroupName),
                ConversationHistory = conversationHistory,
                CopilotCliEndpoint = cliEndpoint
            };

            logger.LogInformation("Invoking agent for group {Group} with model {Model}", message.GroupName, model);

            // Stream response chunks as they arrive
            var streamedContent = new System.Text.StringBuilder();
            var agentStopwatch = Stopwatch.StartNew();
            var response = await agentRunner.RunAgentAsync(request, async chunk =>
            {
                streamedContent.Append(chunk);
                await frontend.SendStreamChunkAsync(message.GroupName, chunk, ct);
            }, ct);
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

    private static string FormatConversationHistory(IReadOnlyList<MessageEntity> messages)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in messages)
            sb.AppendLine($"[{m.Sender}]: {m.Content}");
        return sb.ToString();
    }
}
