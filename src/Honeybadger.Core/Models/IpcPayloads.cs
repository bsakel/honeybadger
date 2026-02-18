namespace Honeybadger.Core.Models;

public class SendMessagePayload
{
    public string Content { get; init; } = string.Empty;
}

public class ScheduleTaskPayload
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ScheduleType { get; init; } = "once";
    public string? CronExpression { get; init; }
    public string? IntervalSeconds { get; init; }
    public string? RunAt { get; init; }
    public string? TimeZone { get; init; }
}

public class TaskControlPayload
{
    public int TaskId { get; init; }
}

public class ListTasksResponsePayload
{
    public List<TaskSummary> Tasks { get; init; } = [];
}

public class TaskSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ScheduleType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? NextRunAt { get; init; }
}

public record DelegateToAgentPayload
{
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public required string Task { get; init; }
    public string? Context { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
}

public record AgentDelegationResponse
{
    public bool Success { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
}

public record UpdateMemoryPayload
{
    public required string Content { get; init; }
    public string? Section { get; init; }
    public string? AgentId { get; init; }
}
