using Honeybadger.Core.Models;

namespace Honeybadger.Core.Interfaces;

public interface IIpcTransport
{
    /// <summary>Start watching for IPC files. Calls the handler for each message received.</summary>
    Task StartAsync(Func<IpcMessage, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>Stop watching.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
