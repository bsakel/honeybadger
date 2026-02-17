namespace Honeybadger.Data.Entities;

public enum ScheduleTypeData { Cron, Interval, Once }
public enum TaskStatusData { Active, Paused, Completed, Cancelled }

public class ScheduledTaskEntity
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ScheduleTypeData ScheduleType { get; set; }
    public string? CronExpression { get; set; }
    public long? IntervalTicks { get; set; }
    public DateTimeOffset? RunAt { get; set; }
    public string? TimeZone { get; set; }
    public TaskStatusData Status { get; set; } = TaskStatusData.Active;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TaskRunLogEntity> RunLogs { get; set; } = [];
}
