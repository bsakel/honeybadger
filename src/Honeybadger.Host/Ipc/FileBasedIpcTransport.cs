using System.Text.Json;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Ipc;

/// <summary>
/// Watches for JSON IPC files in the IPC directory.
/// Uses FileSystemWatcher + 500 ms polling fallback (NTFS filesystem compat).
/// </summary>
public class FileBasedIpcTransport : IIpcTransport, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly string _ipcDirectory;
    private readonly ILogger<FileBasedIpcTransport> _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private Func<IpcMessage, Task>? _handler;
    private readonly HashSet<string> _processing = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string IpcDirectory => _ipcDirectory;

    public FileBasedIpcTransport(string ipcDirectory, ILogger<FileBasedIpcTransport> logger)
    {
        _ipcDirectory = ipcDirectory;
        _logger = logger;
        Directory.CreateDirectory(ipcDirectory);
    }

    public Task StartAsync(Func<IpcMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        _handler = handler;
        _logger.LogInformation("IPC transport watching {Directory}", _ipcDirectory);

        // Primary: FileSystemWatcher
        try
        {
            _watcher = new FileSystemWatcher(_ipcDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FileSystemWatcher failed to start, relying on polling only");
        }

        // Fallback: 500 ms polling
        _pollTimer = new Timer(_ => PollDirectory(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("IPC transport stopping");
        _watcher?.Dispose();
        _pollTimer?.Dispose();
        return Task.CompletedTask;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        _ = ProcessFileAsync(e.FullPath).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger.LogError(t.Exception, "Error processing IPC file {File}", e.FullPath);
        }, TaskScheduler.Default);
    }

    private void PollDirectory()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_ipcDirectory, "*.json"))
            {
                _ = ProcessFileAsync(file).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.LogError(t.Exception, "Error processing IPC file {File}", file);
                }, TaskScheduler.Default);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Poll error");
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        await _lock.WaitAsync();
        try
        {
            if (_processing.Contains(filePath)) return;
            _processing.Add(filePath);
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            // Small delay to ensure write is complete
            await Task.Delay(50);

            if (!File.Exists(filePath)) return;

            string json;
            try
            {
                json = await File.ReadAllTextAsync(filePath);
            }
            catch (IOException)
            {
                // File still being written — will retry on next poll
                return;
            }

            var message = JsonSerializer.Deserialize<IpcMessage>(json, JsonOpts);
            if (message is null)
            {
                _logger.LogWarning("Failed to deserialize IPC file: {Path}", filePath);
                return;
            }

            // Atomic delete before dispatching (crash-safe)
            try { File.Delete(filePath); }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not delete IPC file {File}", filePath);
            }

            _logger.LogDebug("IPC file {Id} ({Type}) [CorrelationId={CorrelationId}]",
                message.Id, message.Type, message.CorrelationId);
            if (_handler is not null)
                await _handler(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing IPC file {Path}", filePath);
        }
        finally
        {
            await _lock.WaitAsync();
            try { _processing.Remove(filePath); }
            finally { _lock.Release(); }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _pollTimer?.Dispose();
        _lock.Dispose();
    }
}
