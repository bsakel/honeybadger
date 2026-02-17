using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Scheduling;

/// <summary>
/// Serializes agent invocations per group while allowing cross-group parallelism.
/// Each group gets its own unbounded channel consumed by a single background loop.
/// A global semaphore caps total concurrent agent invocations.
/// </summary>
public sealed class GroupQueue(int maxConcurrent, ILogger<GroupQueue> logger) : IDisposable
{
    private readonly SemaphoreSlim _globalSemaphore = new(maxConcurrent, maxConcurrent);
    private readonly CancellationTokenSource _shutdownCts = new();

    // Lazy ensures the consumer task is started exactly once per group, thread-safely.
    private readonly ConcurrentDictionary<string, Lazy<Channel<Func<CancellationToken, Task>>>> _groups = new();

    /// <summary>
    /// Enqueue work for a group. Work items for the same group run serially;
    /// work items for different groups may run concurrently up to maxConcurrent.
    /// </summary>
    public async Task EnqueueAsync(string groupName, Func<CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        var lazyChannel = _groups.GetOrAdd(groupName,
            name => new Lazy<Channel<Func<CancellationToken, Task>>>(
                () => StartGroupConsumer(name),
                LazyThreadSafetyMode.ExecutionAndPublication));

        await lazyChannel.Value.Writer.WriteAsync(work, ct);
        logger.LogDebug("GroupQueue: enqueued work for group {Group}", groupName);
    }

    private Channel<Func<CancellationToken, Task>> StartGroupConsumer(string groupName)
    {
        var channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
            new UnboundedChannelOptions { SingleReader = true });
        _ = ConsumeGroupAsync(groupName, channel.Reader, _shutdownCts.Token);
        logger.LogDebug("Started consumer for group {Group}", groupName);
        return channel;
    }

    private async Task ConsumeGroupAsync(
        string groupName,
        ChannelReader<Func<CancellationToken, Task>> reader,
        CancellationToken ct)
    {
        try
        {
            await foreach (var work in reader.ReadAllAsync(ct))
            {
                logger.LogDebug("Waiting for global semaphore (group {Group})", groupName);
                await _globalSemaphore.WaitAsync(ct);
                logger.LogDebug("Semaphore acquired for group {Group}", groupName);

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await work(ct);
                    stopwatch.Stop();
                    logger.LogDebug("Work completed for group {Group} in {ElapsedMs}ms",
                        groupName, stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stopwatch.Stop();
                    logger.LogError(ex, "GroupQueue: unhandled error in group {Group} after {ElapsedMs}ms",
                        groupName, stopwatch.ElapsedMilliseconds);
                }
                finally
                {
                    _globalSemaphore.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("GroupQueue: consumer for group {Group} stopped", groupName);
        }
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _globalSemaphore.Dispose();
    }
}
