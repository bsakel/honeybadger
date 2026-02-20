using Honeybadger.Core.Configuration;
using Microsoft.Extensions.AI;

namespace Honeybadger.Core.Interfaces;

/// <summary>
/// Provides AIFunction tools for a specific agent invocation.
/// Register implementations via IServiceCollection.AddSingleton&lt;IToolProvider, MyProvider&gt;()
/// and opt in from Program.cs. AgentToolFactory collects all registered providers automatically.
/// </summary>
public interface IToolProvider
{
    IEnumerable<AIFunction> GetTools(AgentConfiguration agentConfig, string groupName, string correlationId);
}
