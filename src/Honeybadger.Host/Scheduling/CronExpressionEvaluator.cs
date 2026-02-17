using Cronos;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Scheduling;

/// <summary>
/// Evaluates cron expressions, intervals, and one-shot schedules.
/// </summary>
public class CronExpressionEvaluator(ILogger<CronExpressionEvaluator> logger)
{
    public DateTimeOffset? GetNextOccurrence(string cronExpression, DateTimeOffset from, string? timeZone = null)
    {
        try
        {
            var tz = timeZone is not null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
            var expression = CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);
            var next = expression.GetNextOccurrence(from.UtcDateTime, tz);
            return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse cron expression: {Expression}", cronExpression);
            return null;
        }
    }

    public DateTimeOffset GetNextInterval(DateTimeOffset lastRun, TimeSpan interval)
        => lastRun + interval;
}
