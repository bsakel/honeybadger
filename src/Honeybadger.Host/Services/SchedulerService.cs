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
/// Polls the database for due scheduled tasks and spawns agents.
/// Uses a 30-second tick; cron/interval tasks compute their next run after execution,
/// once tasks are marked completed.
/// </summary>
public class SchedulerService(
    IAgentRunner agentRunner,
    CronExpressionEvaluator cronEval,
    HierarchicalMemoryStore memoryStore,
    IServiceScopeFactory scopeFactory,
    IOptions<AgentOptions> agentOptions,
    IOptions<HoneybadgerOptions> honeybadgerOptions,
    ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SchedulerService started");

        // Run an immediate tick on startup to catch any tasks overdue from a previous crash.
        try
        {
            await TickAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SchedulerService startup tick error");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SchedulerService tick error");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        logger.LogDebug("Tick at {Time}", DateTimeOffset.UtcNow.ToString("HH:mm:ss"));

        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<TaskRepository>();

        var dueTasks = await taskRepo.GetDueAsync(DateTimeOffset.UtcNow, ct);
        if (dueTasks.Count == 0)
        {
            logger.LogDebug("SchedulerService: no due tasks");
            return;
        }

        logger.LogInformation("SchedulerService: {Count} due task(s)", dueTasks.Count);

        // Run all due tasks concurrently
        await Task.WhenAll(dueTasks.Select(task => RunTaskAsync(task, ct)));
    }

    private async Task RunTaskAsync(ScheduledTaskEntity task, CancellationToken ct)
    {
        // Generate CorrelationId for this scheduled task execution
        var correlationId = Guid.NewGuid().ToString("N")[..12];

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        using var __ = LogContext.PushProperty("TaskId", task.Id);
        using var ___ = LogContext.PushProperty("GroupName", task.GroupName);

        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var log = new TaskRunLogEntity
        {
            TaskId = task.Id,
            StartedAt = startedAt,
            Status = RunStatus.Success
        };

        try
        {
            logger.LogInformation("Running scheduled task {Id} '{Name}' for group {Group}",
                task.Id, task.Name, task.GroupName);

            var opts = agentOptions.Value;
            var model = honeybadgerOptions.Value.GetModelForGroup(task.GroupName);
            var cliEndpoint = opts.CopilotCli.AutoStart
                ? $"localhost:{opts.CopilotCli.Port}"
                : string.Empty;

            var request = new AgentRequest
            {
                CorrelationId = correlationId,
                MessageId = Guid.NewGuid().ToString(),
                GroupName = task.GroupName,
                Content = BuildTaskPrompt(task),
                Model = model,
                GlobalMemory = memoryStore.LoadGlobalMemory(),
                GroupMemory = memoryStore.LoadGroupMemory(task.GroupName),
                CopilotCliEndpoint = cliEndpoint
            };

            var response = await agentRunner.RunAgentAsync(request, onStreamChunk: null, ct);

            log.Status = response.Success ? RunStatus.Success : RunStatus.Failure;
            log.Result = response.Content;
            log.Error = response.Error;

            stopwatch.Stop();
            logger.LogDebug("Scheduled task {Id} completed in {ElapsedMs}ms: {Status}",
                task.Id, stopwatch.ElapsedMilliseconds, log.Status);
            logger.LogInformation("Scheduled task {Id} completed: {Status}", task.Id, log.Status);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            log.Status = RunStatus.Timeout;
            logger.LogWarning("Scheduled task {Id} timed out after {ElapsedMs}ms",
                task.Id, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            log.Status = RunStatus.Failure;
            log.Error = ex.Message;
            logger.LogError(ex, "Scheduled task {Id} failed after {ElapsedMs}ms",
                task.Id, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            log.CompletedAt = DateTimeOffset.UtcNow;
            log.DurationMs = (long)(log.CompletedAt.Value - startedAt).TotalMilliseconds;
        }

        // Compute next run and update task
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextRunAt = task.ScheduleType switch
        {
            ScheduleTypeData.Cron when task.CronExpression is not null
                => cronEval.GetNextOccurrence(task.CronExpression, now, task.TimeZone),
            ScheduleTypeData.Interval when task.IntervalTicks is not null
                => cronEval.GetNextInterval(now, TimeSpan.FromTicks(task.IntervalTicks.Value)),
            _ => null
        };

        var newStatus = task.ScheduleType == ScheduleTypeData.Once
            ? TaskStatusData.Completed
            : TaskStatusData.Active;

        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
        await taskRepo.UpdateAfterRunAsync(task.Id, now, nextRunAt, newStatus, ct);
        await taskRepo.AddRunLogAsync(log, ct);
    }

    private static string BuildTaskPrompt(ScheduledTaskEntity task)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Execute scheduled task: {task.Name}.");
        if (!string.IsNullOrWhiteSpace(task.Description))
            sb.Append($" {task.Description}");
        return sb.ToString();
    }
}
