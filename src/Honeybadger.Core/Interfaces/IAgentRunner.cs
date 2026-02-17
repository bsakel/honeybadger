using Honeybadger.Core.Models;

namespace Honeybadger.Core.Interfaces;

public interface IAgentRunner
{
    /// <summary>
    /// Run an agent with the given request and return the response.
    /// Optionally stream chunks as they arrive via the onStreamChunk callback.
    /// </summary>
    Task<AgentResponse> RunAgentAsync(
        AgentRequest request,
        Func<string, Task>? onStreamChunk = null,
        CancellationToken cancellationToken = default);
}
