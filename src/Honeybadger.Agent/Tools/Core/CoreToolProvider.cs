using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Agent.Tools.Core;

/// <summary>
/// Provides the built-in IPC tools (send_message, schedule_task, etc.) and
/// delegation tools (delegate_to_agent, list_available_agents) for router agents.
/// Register via AddCoreTools() in Program.cs.
/// </summary>
public class CoreToolProvider : IToolProvider
{
    private readonly string _ipcDirectory;
    private readonly ILoggerFactory _loggerFactory;

    public CoreToolProvider(string ipcDirectory, ILoggerFactory loggerFactory)
    {
        _ipcDirectory = ipcDirectory;
        _loggerFactory = loggerFactory;
    }

    public IEnumerable<AIFunction> GetTools(AgentConfiguration agentConfig, string groupName, string correlationId)
    {
        var ipcTools = new IpcTools(
            _ipcDirectory,
            groupName,
            _loggerFactory.CreateLogger<IpcTools>(),
            correlationId,
            agentConfig.AgentId);

        foreach (var tool in ipcTools.GetAll())
            yield return tool;

        if (agentConfig.IsRouter)
        {
            var delegationTools = new AgentDelegationTools(
                _ipcDirectory,
                groupName,
                _loggerFactory.CreateLogger<AgentDelegationTools>(),
                correlationId);

            foreach (var tool in delegationTools.GetAll())
                yield return tool;
        }
    }
}
