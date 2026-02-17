using System.Text.Json;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data.Entities;
using Honeybadger.Data.Repositories;
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
}
