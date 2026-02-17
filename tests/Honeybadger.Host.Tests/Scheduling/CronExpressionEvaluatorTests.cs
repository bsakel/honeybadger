using Honeybadger.Host.Scheduling;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honeybadger.Host.Tests.Scheduling;

public class CronExpressionEvaluatorTests
{
    private readonly CronExpressionEvaluator _evaluator = new(NullLogger<CronExpressionEvaluator>.Instance);

    [Fact]
    public void GetNextOccurrence_EveryMinute_ReturnsNextMinute()
    {
        var from = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var result = _evaluator.GetNextOccurrence("0 * * * * *", from);
        result.Should().NotBeNull();
        result!.Value.Should().BeAfter(from);
    }

    [Fact]
    public void GetNextOccurrence_InvalidExpression_ReturnsNull()
    {
        var from = DateTimeOffset.UtcNow;
        var result = _evaluator.GetNextOccurrence("not-a-cron", from);
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextInterval_AddsIntervalToLastRun()
    {
        var lastRun = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromHours(1);
        var result = _evaluator.GetNextInterval(lastRun, interval);
        result.Should().Be(lastRun + interval);
    }
}
