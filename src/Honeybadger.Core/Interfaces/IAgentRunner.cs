using Honeybadger.Core.Models;
using Microsoft.Extensions.AI;

namespace Honeybadger.Core.Interfaces;

public interface IAgentRunner
{
    /// <summary>
    /// Run an agent with the given request and return the response.
    /// Optionally stream chunks as they arrive via the onStreamChunk callback.
    /// Tools can be provided for multi-agent mode, or null for legacy mode.
    /// </summary>
    Task<AgentResponse> RunAgentAsync(
        AgentRequest request,
        Func<string, Task>? onStreamChunk = null,
        CancellationToken cancellationToken = default,
        IEnumerable<AIFunction>? tools = null);
}
