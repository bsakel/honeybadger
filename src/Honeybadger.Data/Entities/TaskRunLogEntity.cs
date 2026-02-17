namespace Honeybadger.Data.Entities;

public enum RunStatus { Success, Failure, Timeout }

public class TaskRunLogEntity
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public RunStatus Status { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }

    public ScheduledTaskEntity Task { get; set; } = null!;
}
