namespace Honeybadger.Core.Models;

public enum ScheduleType
{
    Cron,
    Interval,
    Once
}

public enum ScheduledTaskStatus
{
    Active,
    Paused,
    Completed,
    Cancelled
}
