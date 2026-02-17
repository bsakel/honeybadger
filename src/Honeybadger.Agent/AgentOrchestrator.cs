using GitHub.Copilot.SDK;
using Honeybadger.Agent.Tools;
using Honeybadger.Core.Models;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Honeybadger.Agent;

/// <summary>
/// Orchestrates agent invocation via the GitHub Copilot SDK:
///  1. Connects to the host Copilot CLI via CliUrl (localhost:PORT)
///  2. Creates or resumes a session with the appropriate model, tools, and system context
///  3. Sends the user message, collects the response via SDK events
///  4. Returns the response with updated session ID for continuity
/// </summary>
public class AgentOrchestrator
{
    private readonly IpcTools _ipcTools;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(IpcTools ipcTools, ILogger<AgentOrchestrator> logger)
    {
        _ipcTools = ipcTools;
        _logger = logger;
    }

    public async Task<AgentResponse> RunAsync(
        AgentRequest request,
        Func<string, Task>? onChunk = null,
        CancellationToken ct = default)
    {
        using var _ = LogContext.PushProperty("CorrelationId", request.CorrelationId);

        _logger.LogDebug("RunAsync starting [Group={Group}, Model={Model}]",
            request.GroupName, request.Model ?? "default");

        if (string.IsNullOrWhiteSpace(request.CopilotCliEndpoint))
        {
            _logger.LogDebug("No CopilotCliEndpoint — returning stub response");
            // No CLI endpoint configured — return stub (useful for testing without AI)
            return new AgentResponse
            {
                Success = true,
                Content = $"[No AI backend] Received: {request.Content}",
                SessionId = request.SessionId ?? Guid.NewGuid().ToString()
            };
        }

        try
        {
            _logger.LogDebug("Creating CopilotClient for {Endpoint}", request.CopilotCliEndpoint);
            await using var client = new CopilotClient(new CopilotClientOptions
            {
                CliUrl = request.CopilotCliEndpoint,
                AutoStart = false,
                UseStdio = false
            });

            await client.StartAsync(ct);
            _logger.LogDebug("CopilotClient connected");

            // Always create a new session to ensure tools are properly registered.
            // The Copilot SDK may not preserve tools when resuming sessions.
            // We still track SessionId in the database for conversation continuity via history.
            _logger.LogDebug("Creating new session with tools");
            var session = await CreateNewSessionAsync(client, request, ct);

            _logger.LogDebug("Created session {SessionId}", session.SessionId);
            await using var __ = session;

            var chunks = new System.Text.StringBuilder();
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.On(evt =>
            {
                if (evt is AssistantMessageEvent msg)
                {
                    var content = msg.Data.Content;
                    chunks.Append(content);

                    _logger.LogTrace("Chunk received ({Length} chars, total {Total})",
                        content.Length, chunks.Length);

                    // Stream chunk to caller if callback provided
                    if (onChunk is not null)
                    {
                        try
                        {
                            onChunk(content).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Streaming chunk callback error");
                        }
                    }

                    done.TrySetResult(chunks.ToString());
                }
            });

            _logger.LogDebug("Message sent to session, awaiting response");
            await session.SendAsync(new MessageOptions { Prompt = request.Content });
            var responseText = await done.Task.WaitAsync(ct);

            _logger.LogDebug("Response received ({Length} chars)", responseText.Length);

            return new AgentResponse
            {
                Success = true,
                Content = responseText,
                SessionId = session.SessionId
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Agent invocation timed out");
            return new AgentResponse { Success = false, Error = "Agent invocation timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent invocation failed");
            return new AgentResponse { Success = false, Error = $"Agent invocation failed: {ex.Message}" };
        }
    }

    private Task<CopilotSession> CreateNewSessionAsync(CopilotClient client, AgentRequest request, CancellationToken ct)
        => client.CreateSessionAsync(new SessionConfig
        {
            Model = request.Model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = BuildSystemContext(request)
            },
            Tools = [.. _ipcTools.GetAll()]
        }, ct);

    private static string BuildSystemContext(AgentRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Group: {request.GroupName}");

        if (!string.IsNullOrWhiteSpace(request.GlobalMemory))
        {
            sb.AppendLine();
            sb.AppendLine("## Global Context");
            sb.AppendLine(request.GlobalMemory);
        }

        if (!string.IsNullOrWhiteSpace(request.GroupMemory))
        {
            sb.AppendLine();
            sb.AppendLine("## Group Context");
            sb.AppendLine(request.GroupMemory);
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationHistory))
        {
            sb.AppendLine();
            sb.AppendLine("## Recent Conversation");
            sb.AppendLine(request.ConversationHistory);
        }

        sb.AppendLine();
        sb.AppendLine("You have tools: send_message, schedule_task, pause_task, resume_task, cancel_task, list_tasks.");

        return sb.ToString();
    }
}
