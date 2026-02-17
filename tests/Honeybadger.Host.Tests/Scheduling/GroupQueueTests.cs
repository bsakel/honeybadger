using FluentAssertions;
using Honeybadger.Host.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honeybadger.Host.Tests.Scheduling;

public class GroupQueueTests
{
    [Fact]
    public async Task SameGroup_ExecutesSerially()
    {
        using var queue = new GroupQueue(maxConcurrent: 2, NullLogger<GroupQueue>.Instance);
        var results = new List<int>();
        var tcs = new TaskCompletionSource();

        // Enqueue two items for the same group — second must wait for first
        await queue.EnqueueAsync("g1", async ct =>
        {
            results.Add(1);
            await Task.Delay(20, ct);
            results.Add(2);
        });

        await queue.EnqueueAsync("g1", ct =>
        {
            results.Add(3);
            return Task.CompletedTask;
        });

        // Give enough time for both to complete
        await Task.Delay(200);

        results.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task DifferentGroups_ExecuteConcurrently()
    {
        using var queue = new GroupQueue(maxConcurrent: 3, NullLogger<GroupQueue>.Instance);
        var startOrder = new List<string>();
        var barrier = new SemaphoreSlim(0, 2);

        await queue.EnqueueAsync("g1", async ct =>
        {
            lock (startOrder) startOrder.Add("g1-start");
            barrier.Release();
            await Task.Delay(50, ct);
            lock (startOrder) startOrder.Add("g1-end");
        });

        await queue.EnqueueAsync("g2", async ct =>
        {
            lock (startOrder) startOrder.Add("g2-start");
            barrier.Release();
            await Task.Delay(50, ct);
            lock (startOrder) startOrder.Add("g2-end");
        });

        // Wait for both to start (concurrent)
        var both = await Task.WhenAll(
            barrier.WaitAsync(TimeSpan.FromSeconds(2)),
            barrier.WaitAsync(TimeSpan.FromSeconds(2)));
        both.Should().AllBeEquivalentTo(true, "both groups should start before either finishes");

        await Task.Delay(150);
        startOrder.Should().Contain("g1-end").And.Contain("g2-end");
    }

    [Fact]
    public async Task GlobalSemaphore_LimitsConcurrency()
    {
        using var queue = new GroupQueue(maxConcurrent: 1, NullLogger<GroupQueue>.Instance);
        var concurrent = 0;
        var maxObserved = 0;

        var tasks = new List<Task>();
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            tasks.Add(queue.EnqueueAsync($"g{idx}", async ct =>
            {
                var c = Interlocked.Increment(ref concurrent);
                lock (tasks) maxObserved = Math.Max(maxObserved, c);
                await Task.Delay(30, ct);
                Interlocked.Decrement(ref concurrent);
            }));
        }
        await Task.WhenAll(tasks);

        await Task.Delay(250);
        maxObserved.Should().Be(1, "only 1 concurrent agent allowed");
    }
}
