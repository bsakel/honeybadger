using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Microsoft.Extensions.AI;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Creates AIFunction tool instances for agents by delegating to all registered IToolProviders.
/// New tool sets are added by registering additional IToolProvider implementations via
/// IServiceCollection (e.g. AddCoreTools(), AddSdlcTools()) — no changes needed here.
/// </summary>
public class AgentToolFactory(IEnumerable<IToolProvider> toolProviders)
{
    public IEnumerable<AIFunction> CreateToolsForAgent(
        AgentConfiguration agentConfig,
        string groupName,
        string correlationId)
    {
        return toolProviders.SelectMany(p => p.GetTools(agentConfig, groupName, correlationId));
    }
}
