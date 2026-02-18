using System.Text.Json;
using Honeybadger.Agent;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data.Entities;
using Honeybadger.Data.Repositories;
using Honeybadger.Host.Agents;
using Honeybadger.Host.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Honeybadger.Host.Services;

/// <summary>
/// Watches for IPC files from agents and routes commands:
///   SendMessage → IChatFrontend
///   ScheduleTask → DB
///   PauseTask/ResumeTask/CancelTask → DB
///   ListTasks → writes response file
/// </summary>
public class IpcWatcherService(
    IIpcTransport ipcTransport,
    IChatFrontend frontend,
    CronExpressionEvaluator cronEval,
    IServiceScopeFactory scopeFactory,
    AgentRegistry agentRegistry,
    AgentToolFactory agentToolFactory,
    ILoggerFactory loggerFactory,
    HoneybadgerOptions honeybadgerOptions,
    string ipcDirectory,
    ILogger<IpcWatcherService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IpcWatcherService started");

        await ipcTransport.StartAsync(message => HandleAsync(message, stoppingToken), stoppingToken);
    }

    private async Task HandleAsync(IpcMessage message, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("CorrelationId", message.CorrelationId);

        logger.LogInformation("IPC {Type} from group {Group}", message.Type, message.GroupName);

        try
        {
            switch (message.Type)
            {
                case IpcMessageType.SendMessage:
                    await HandleSendMessageAsync(message, ct);
                    break;
                case IpcMessageType.ScheduleTask:
                    await HandleScheduleTaskAsync(message, ct);
                    break;
                case IpcMessageType.PauseTask:
                case IpcMessageType.ResumeTask:
                case IpcMessageType.CancelTask:
                    await HandleTaskControlAsync(message, ct);
                    break;
                case IpcMessageType.ListTasks:
                    await HandleListTasksAsync(message, ct);
                    break;
                case IpcMessageType.DelegateToAgent:
                    await HandleDelegateToAgentAsync(message, ct);
                    break;
                case IpcMessageType.ListAvailableAgents:
                    await HandleListAvailableAgentsAsync(message, ct);
                    break;
                case IpcMessageType.UpdateMemory:
                    await HandleUpdateMemoryAsync(message, ct);
                    break;
                default:
                    logger.LogWarning("Unknown IPC message type: {Type}", message.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle IPC message {Id} ({Type})", message.Id, message.Type);
        }
    }

    private async Task HandleSendMessageAsync(IpcMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SendMessagePayload>(message.Payload, JsonOpts);
        if (payload is null)
        {
            logger.LogWarning("Invalid SendMessage payload from {Group}", message.GroupName);
            return;
        }

        var chatMessage = new ChatMessage
        {
            GroupName = message.GroupName,
            Content = payload.Content,
            Sender = "agent",
            IsFromAgent = true
        };

        await frontend.SendToUserAsync(chatMessage, ct);

        // Persist to DB
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<MessageRepository>();
        await repo.AddMessageAsync(message.GroupName, chatMessage.Id, "agent", payload.Content, true, ct);
    }

    private async Task HandleScheduleTaskAsync(IpcMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ScheduleTaskPayload>(message.Payload, JsonOpts);
        if (payload is null)
        {
            logger.LogWarning("Invalid ScheduleTask payload from {Group}", message.GroupName);
            return;
        }

        var scheduleType = payload.ScheduleType.ToLowerInvariant() switch
        {
            "cron" => ScheduleTypeData.Cron,
            "interval" => ScheduleTypeData.Interval,
            _ => ScheduleTypeData.Once
        };

        var interval = payload.IntervalSeconds is not null
            ? TimeSpan.FromSeconds(double.Parse(payload.IntervalSeconds))
            : (TimeSpan?)null;
        var runAt = payload.RunAt is not null ? DateTimeOffset.Parse(payload.RunAt) : (DateTimeOffset?)null;
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset? nextRunAt = scheduleType switch
        {
            ScheduleTypeData.Cron when payload.CronExpression is not null
                => cronEval.GetNextOccurrence(payload.CronExpression, now, payload.TimeZone),
            ScheduleTypeData.Interval when interval is not null
                => now + interval.Value,
            ScheduleTypeData.Once => runAt,
            _ => null
        };

        var entity = new ScheduledTaskEntity
        {
            GroupName = message.GroupName,
            Name = payload.Name,
            Description = payload.Description,
            ScheduleType = scheduleType,
            CronExpression = payload.CronExpression,
            IntervalTicks = interval is not null ? (long)interval.Value.Ticks : null,
            RunAt = runAt,
            TimeZone = payload.TimeZone,
            NextRunAt = nextRunAt,
            Status = TaskStatusData.Active,
            CreatedAt = now
        };

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
        await repo.AddAsync(entity, ct);

        logger.LogInformation("Scheduled task '{Name}' ({Type}) for group {Group}", payload.Name, scheduleType, message.GroupName);
    }

    private async Task HandleTaskControlAsync(IpcMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TaskControlPayload>(message.Payload, JsonOpts);
        if (payload is null || payload.TaskId <= 0)
        {
            logger.LogWarning("Invalid task control payload from {Group}", message.GroupName);
            return;
        }

        var newStatus = message.Type switch
        {
            IpcMessageType.PauseTask => TaskStatusData.Paused,
            IpcMessageType.ResumeTask => TaskStatusData.Active,
            IpcMessageType.CancelTask => TaskStatusData.Cancelled,
            _ => throw new InvalidOperationException()
        };

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
        await repo.UpdateStatusAsync(payload.TaskId, newStatus, ct);

        logger.LogInformation("Task {Id} status → {Status}", payload.TaskId, newStatus);
    }

    private async Task HandleListTasksAsync(IpcMessage message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
        var tasks = await repo.GetByGroupAsync(message.GroupName, ct);

        var summaries = tasks.Select(t => new TaskSummary
        {
            Id = t.Id,
            Name = t.Name,
            ScheduleType = t.ScheduleType.ToString(),
            Status = t.Status.ToString(),
            NextRunAt = t.NextRunAt?.ToString("O")
        }).ToList();

        var response = new ListTasksResponsePayload { Tasks = summaries };
        var responseJson = JsonSerializer.Serialize(response, JsonOpts);

        // Write response file for agent to poll
        var responseFileName = $"{message.Id}.response.json";
        var responsePath = Path.Combine(ipcDirectory, responseFileName);
        await File.WriteAllTextAsync(responsePath, responseJson, ct);

        logger.LogInformation("ListTasks response written for group {Group}: {Count} tasks", message.GroupName, summaries.Count);
    }

    private async Task HandleDelegateToAgentAsync(IpcMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<DelegateToAgentPayload>(message.Payload, JsonOpts);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AgentId) || string.IsNullOrWhiteSpace(payload.Task))
        {
            logger.LogWarning("Invalid DelegateToAgent payload from {Group}", message.GroupName);
            await WriteErrorResponseAsync(payload?.RequestId ?? Guid.NewGuid().ToString(), "Invalid delegation payload", ct);
            return;
        }

        logger.LogInformation("Delegating to agent {AgentId} for group {Group}", payload.AgentId, message.GroupName);

        // Validate agent exists
        if (!agentRegistry.TryGetAgent(payload.AgentId, out var agentConfig))
        {
            logger.LogWarning("Agent {AgentId} not found in registry", payload.AgentId);
            await WriteErrorResponseAsync(payload.RequestId, $"Agent '{payload.AgentId}' not found", ct);
            return;
        }

        try
        {
            // Load soul file
            var soul = agentRegistry.LoadSoulFile(agentConfig!.Soul);

            // Build AgentRequest for specialist
            var model = agentConfig.Model ?? honeybadgerOptions.Agent.DefaultModel;
            var cliEndpoint = honeybadgerOptions.Agent.CopilotCli.AutoStart
                ? $"localhost:{honeybadgerOptions.Agent.CopilotCli.Port}"
                : string.Empty;

            var request = new AgentRequest
            {
                CorrelationId = message.CorrelationId,
                MessageId = Guid.NewGuid().ToString(),
                GroupName = message.GroupName,
                Content = payload.Task,
                Model = model,
                AgentId = agentConfig.AgentId,
                IsRouterAgent = false,
                Soul = soul,
                AvailableTools = agentConfig.Tools,
                GlobalMemory = payload.Context,
                CopilotCliEndpoint = cliEndpoint
            };

            // Create tools for specialist
            var tools = agentToolFactory.CreateToolsForAgent(agentConfig, message.GroupName, message.CorrelationId);

            // Create and run orchestrator
            var orchestrator = new AgentOrchestrator(tools, loggerFactory.CreateLogger<AgentOrchestrator>());
            var response = await orchestrator.RunAsync(request, onChunk: null, ct);

            // Write response
            var delegationResponse = new AgentDelegationResponse
            {
                Success = response.Success,
                Result = response.Content,
                Error = response.Error
            };

            var responseJson = JsonSerializer.Serialize(delegationResponse, JsonOpts);
            var responsePath = Path.Combine(ipcDirectory, $"{payload.RequestId}.response.json");
            await File.WriteAllTextAsync(responsePath, responseJson, ct);

            logger.LogInformation("Agent {AgentId} delegation completed for group {Group}: Success={Success}",
                payload.AgentId, message.GroupName, response.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error delegating to agent {AgentId}", payload.AgentId);
            await WriteErrorResponseAsync(payload.RequestId, $"Delegation error: {ex.Message}", ct);
        }
    }

    private async Task HandleListAvailableAgentsAsync(IpcMessage message, CancellationToken ct)
    {
        var summary = agentRegistry.GetAgentSummary();
        var responseText = string.IsNullOrWhiteSpace(summary)
            ? "No specialist agents available."
            : summary;

        // Write response file for agent to poll
        var responseFileName = $"{message.Id}.response.json";
        var responsePath = Path.Combine(ipcDirectory, responseFileName);
        await File.WriteAllTextAsync(responsePath, responseText, ct);

        logger.LogInformation("ListAvailableAgents response written for group {Group}", message.GroupName);
    }

    private async Task HandleUpdateMemoryAsync(IpcMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UpdateMemoryPayload>(message.Payload, JsonOpts);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Content))
        {
            logger.LogWarning("Invalid UpdateMemory payload from {Group}", message.GroupName);
            return;
        }

        // Write to groups/{groupName}/MEMORY.md
        var repoRoot = Directory.GetCurrentDirectory();
        var memoryDir = Path.Combine(repoRoot, "groups", message.GroupName);
        Directory.CreateDirectory(memoryDir);
        var memoryPath = Path.Combine(memoryDir, "MEMORY.md");

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var agent = payload.AgentId ?? "unknown";
        var section = payload.Section ?? "Notes";

        var entry = $"\n## {section} ({agent}, {timestamp})\n- {payload.Content}\n";
        await File.AppendAllTextAsync(memoryPath, entry, ct);

        logger.LogInformation("Memory updated for group {Group} by {Agent}", message.GroupName, agent);
    }

    private async Task WriteErrorResponseAsync(string requestId, string error, CancellationToken ct)
    {
        var response = new AgentDelegationResponse
        {
            Success = false,
            Error = error
        };

        var responseJson = JsonSerializer.Serialize(response, JsonOpts);
        var responsePath = Path.Combine(ipcDirectory, $"{requestId}.response.json");
        await File.WriteAllTextAsync(responsePath, responseJson, ct);
    }
}
