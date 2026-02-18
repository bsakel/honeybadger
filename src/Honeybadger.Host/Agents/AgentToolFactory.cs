using Honeybadger.Agent.Tools;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Creates AIFunction tool instances for agents based on their configuration.
/// Maps tool names from agent config → actual AIFunction instances.
/// </summary>
public class AgentToolFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _ipcDirectory;

    public AgentToolFactory(ILoggerFactory loggerFactory, string ipcDirectory)
    {
        _loggerFactory = loggerFactory;
        _ipcDirectory = ipcDirectory;
    }

    /// <summary>
    /// Create tool instances for an agent based on its configuration.
    /// Returns all tools from IpcTools, and delegation tools if agent is a router.
    /// Note: Fine-grained filtering by tool name would require matching against AIFunction,
    /// but for now we provide all IPC tools and let the agent use what it needs.
    /// </summary>
    public IEnumerable<AIFunction> CreateToolsForAgent(
        AgentConfiguration agentConfig,
        string groupName,
        string correlationId)
    {
        var logger = _loggerFactory.CreateLogger<AgentToolFactory>();

        // Check if agent needs any IPC tools
        var hasIpcTools = agentConfig.Tools.Any(t => t.ToLowerInvariant() switch
        {
            "send_message" or "schedule_task" or "list_tasks" or
            "pause_task" or "resume_task" or "cancel_task" => true,
            _ => false
        });

        if (hasIpcTools)
        {
            var ipcTools = new IpcTools(
                _ipcDirectory,
                groupName,
                _loggerFactory.CreateLogger<IpcTools>(),
                correlationId);

            foreach (var tool in ipcTools.GetAll())
            {
                yield return tool;
            }
        }

        // Add delegation tools for router agents
        if (agentConfig.IsRouter)
        {
            var delegationTools = new AgentDelegationTools(
                _ipcDirectory,
                groupName,
                _loggerFactory.CreateLogger<AgentDelegationTools>(),
                correlationId);

            foreach (var tool in delegationTools.GetAll())
            {
                yield return tool;
            }
        }

        // Warn about unknown tools
        foreach (var toolName in agentConfig.Tools)
        {
            var known = toolName.ToLowerInvariant() switch
            {
                "send_message" or "schedule_task" or "list_tasks" or
                "pause_task" or "resume_task" or "cancel_task" or
                "delegate_to_agent" or "list_available_agents" => true,
                _ => false
            };

            if (!known)
            {
                logger.LogWarning("Unknown tool '{ToolName}' in agent '{AgentId}' configuration",
                    toolName, agentConfig.AgentId);
            }
        }
    }
}
