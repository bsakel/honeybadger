using System.Text.Json;
using Honeybadger.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Agent.Tools;

/// <summary>
/// Custom AIFunction tools the agent can call via tool use.
/// Each tool writes a JSON file to /workspace/ipc/ (bind-mounted to host).
/// The host IpcWatcherService picks them up and routes them.
/// </summary>
public class IpcTools
{
    private readonly string _ipcDirectory;
    private readonly string _groupName;
    private readonly ILogger<IpcTools> _logger;
    private readonly string _correlationId;
    private readonly string _agentId;

    public IpcTools(string ipcDirectory, string groupName, ILogger<IpcTools> logger, string correlationId = "", string agentId = "")
    {
        _ipcDirectory = ipcDirectory;
        _groupName = groupName;
        _logger = logger;
        _correlationId = correlationId;
        _agentId = agentId;
        Directory.CreateDirectory(ipcDirectory);
    }

    public IEnumerable<AIFunction> GetAll() =>
    [
        AIFunctionFactory.Create(SendMessage, "send_message",
            "Send a message to the user in the chat"),
        AIFunctionFactory.Create(ScheduleTask, "schedule_task",
            "Schedule a recurring or one-time task"),
        AIFunctionFactory.Create(PauseTask, "pause_task",
            "Pause a scheduled task by ID"),
        AIFunctionFactory.Create(ResumeTask, "resume_task",
            "Resume a paused scheduled task by ID"),
        AIFunctionFactory.Create(CancelTask, "cancel_task",
            "Cancel a scheduled task by ID"),
        AIFunctionFactory.Create(ListTasks, "list_tasks",
            "List all scheduled tasks for this group"),
        AIFunctionFactory.Create(UpdateMemory, "update_memory",
            "Save a note to the group's persistent memory. Use this when the user asks you to remember something."),
    ];

    private async Task<string> SendMessage(
        [System.ComponentModel.Description("The message content to send to the user")]
        string content)
    {
        _logger.LogDebug("Tool 'send_message' invoked [Group={Group}]", _groupName);
        var payload = new SendMessagePayload { Content = content };
        await WriteIpcFileAsync(IpcMessageType.SendMessage, payload);
        return "Message sent";
    }

    private async Task<string> ScheduleTask(
        [System.ComponentModel.Description("Human-readable task name")]
        string name,
        [System.ComponentModel.Description("Task description")]
        string description,
        [System.ComponentModel.Description("Schedule type: 'cron', 'interval', or 'once'")]
        string scheduleType,
        [System.ComponentModel.Description("Cron expression (when scheduleType=cron), e.g. '0 9 * * MON-FRI'")]
        string? cronExpression = null,
        [System.ComponentModel.Description("Interval in seconds (when scheduleType=interval)")]
        string? intervalSeconds = null,
        [System.ComponentModel.Description("ISO 8601 datetime to run once (when scheduleType=once)")]
        string? runAt = null,
        [System.ComponentModel.Description("IANA timezone name, e.g. 'America/New_York'")]
        string? timeZone = null)
    {
        _logger.LogDebug("Tool 'schedule_task' invoked [Group={Group}]", _groupName);
        var payload = new ScheduleTaskPayload
        {
            Name = name,
            Description = description,
            ScheduleType = scheduleType,
            CronExpression = cronExpression,
            IntervalSeconds = intervalSeconds,
            RunAt = runAt,
            TimeZone = timeZone
        };
        await WriteIpcFileAsync(IpcMessageType.ScheduleTask, payload);
        return $"Task '{name}' scheduled";
    }

    private Task<string> PauseTask(
        [System.ComponentModel.Description("The task ID to pause")]
        int taskId)
    {
        _logger.LogDebug("Tool 'pause_task' invoked [Group={Group}]", _groupName);
        return WriteControlAsync(IpcMessageType.PauseTask, taskId);
    }

    private Task<string> ResumeTask(
        [System.ComponentModel.Description("The task ID to resume")]
        int taskId)
    {
        _logger.LogDebug("Tool 'resume_task' invoked [Group={Group}]", _groupName);
        return WriteControlAsync(IpcMessageType.ResumeTask, taskId);
    }

    private Task<string> CancelTask(
        [System.ComponentModel.Description("The task ID to cancel")]
        int taskId)
    {
        _logger.LogDebug("Tool 'cancel_task' invoked [Group={Group}]", _groupName);
        return WriteControlAsync(IpcMessageType.CancelTask, taskId);
    }

    private async Task<string> WriteControlAsync(IpcMessageType type, int taskId)
    {
        await WriteIpcFileAsync(type, new TaskControlPayload { TaskId = taskId });
        return $"Task {taskId} {type.ToString().ToLower()}d";
    }

    private async Task<string> ListTasks()
    {
        _logger.LogDebug("Tool 'list_tasks' invoked [Group={Group}]", _groupName);
        var requestId = Guid.NewGuid().ToString();
        var message = new IpcMessage
        {
            Id = requestId,
            CorrelationId = _correlationId,
            Type = IpcMessageType.ListTasks,
            GroupName = _groupName,
            Payload = "{}"
        };

        var json = JsonSerializer.Serialize(message);
        var fileName = $"{message.Id}.json";
        var tempPath = Path.Combine(_ipcDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(_ipcDirectory, fileName);

        // Write request file
        _logger.LogDebug("Requesting list_tasks {RequestId}", requestId);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);

        // Poll for response file: {requestId}.response.json
        var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(responseFile))
            {
                try
                {
                    _logger.LogDebug("Received list_tasks response {RequestId}", requestId);
                    var responseJson = await File.ReadAllTextAsync(responseFile);
                    File.Delete(responseFile); // Clean up

                    var response = JsonSerializer.Deserialize<ListTasksResponsePayload>(responseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (response is null || response.Tasks.Count == 0)
                        return "No scheduled tasks found for this group.";

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"Found {response.Tasks.Count} scheduled task(s):");
                    foreach (var task in response.Tasks)
                    {
                        sb.AppendLine($"- ID {task.Id}: {task.Name} ({task.ScheduleType}, {task.Status})");
                        if (!string.IsNullOrEmpty(task.NextRunAt))
                            sb.AppendLine($"  Next run: {task.NextRunAt}");
                    }
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading list_tasks response {RequestId}", requestId);
                    return $"Error reading response: {ex.Message}";
                }
            }

            await Task.Delay(50);
        }

        _logger.LogWarning("Timeout waiting for list_tasks response {RequestId}", requestId);
        return "Timeout waiting for task list response from host";
    }

    private async Task<string> UpdateMemory(
        [System.ComponentModel.Description("The content to save to memory")]
        string content,
        [System.ComponentModel.Description("Optional section name to organize the note under")]
        string? section = null)
    {
        _logger.LogDebug("Tool 'update_memory' invoked [Group={Group}]", _groupName);
        var payload = new UpdateMemoryPayload
        {
            Content = content,
            Section = section,
            AgentId = _agentId
        };
        await WriteIpcFileAsync(IpcMessageType.UpdateMemory, payload);
        return "Memory updated";
    }

    private async Task WriteIpcFileAsync<T>(IpcMessageType type, T payload)
    {
        var message = new IpcMessage
        {
            CorrelationId = _correlationId,
            Type = type,
            GroupName = _groupName,
            Payload = JsonSerializer.Serialize(payload)
        };

        var json = JsonSerializer.Serialize(message);
        var fileName = $"{message.Id}.json";
        var tempPath = Path.Combine(_ipcDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(_ipcDirectory, fileName);

        _logger.LogDebug("Writing IPC file {FileName} (type={Type})", fileName, type);
        // Atomic write: temp + rename
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);
    }
}
