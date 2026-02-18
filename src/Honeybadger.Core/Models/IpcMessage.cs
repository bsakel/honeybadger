namespace Honeybadger.Core.Models;

public enum IpcMessageType
{
    SendMessage,
    ScheduleTask,
    PauseTask,
    ResumeTask,
    CancelTask,
    ListTasks,
    DelegateToAgent,
    ListAvailableAgents,
    UpdateMemory
}

public class IpcMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; init; } = string.Empty;
    public IpcMessageType Type { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
