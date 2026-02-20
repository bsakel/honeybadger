using System.Text.Json;
using Honeybadger.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Agent.Tools.Core;

/// <summary>
/// Multi-agent delegation tools for router agents.
/// Allows router agents to delegate tasks to specialist agents.
/// </summary>
public class AgentDelegationTools
{
    private readonly string _ipcDirectory;
    private readonly string _groupName;
    private readonly ILogger<AgentDelegationTools> _logger;
    private readonly string _correlationId;

    public AgentDelegationTools(string ipcDirectory, string groupName, ILogger<AgentDelegationTools> logger, string correlationId = "")
    {
        _ipcDirectory = ipcDirectory;
        _groupName = groupName;
        _logger = logger;
        _correlationId = correlationId;
        Directory.CreateDirectory(ipcDirectory);
    }

    public IEnumerable<AIFunction> GetAll() =>
    [
        AIFunctionFactory.Create(DelegateToAgent, "delegate_to_agent",
            "Delegate a task to a specialist agent. Use this when a specialist can handle the task better than you."),
        AIFunctionFactory.Create(ListAvailableAgents, "list_available_agents",
            "List all available specialist agents and their capabilities"),
    ];

    private async Task<string> DelegateToAgent(
        [System.ComponentModel.Description("The ID of the specialist agent to delegate to")]
        string agentId,
        [System.ComponentModel.Description("The task for the specialist to perform")]
        string task,
        [System.ComponentModel.Description("Optional additional context for the specialist")]
        string? context = null,
        [System.ComponentModel.Description("Timeout in seconds (default 300)")]
        int timeoutSeconds = 300)
    {
        _logger.LogDebug("Tool 'delegate_to_agent' invoked [AgentId={AgentId}, Group={Group}]", agentId, _groupName);

        var requestId = Guid.NewGuid().ToString();
        var payload = new DelegateToAgentPayload
        {
            RequestId = requestId,
            AgentId = agentId,
            Task = task,
            Context = context,
            TimeoutSeconds = timeoutSeconds
        };

        var message = new IpcMessage
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = _correlationId,
            Type = IpcMessageType.DelegateToAgent,
            GroupName = _groupName,
            Payload = JsonSerializer.Serialize(payload)
        };

        var json = JsonSerializer.Serialize(message);
        var fileName = $"{message.Id}.json";
        var tempPath = Path.Combine(_ipcDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(_ipcDirectory, fileName);

        // Write request file
        _logger.LogDebug("Delegating to agent {AgentId} with RequestId {RequestId}", agentId, requestId);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);

        // Poll for response file: {requestId}.response.json
        var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(responseFile))
            {
                try
                {
                    _logger.LogDebug("Received delegation response {RequestId}", requestId);
                    var responseJson = await File.ReadAllTextAsync(responseFile);
                    File.Delete(responseFile); // Clean up

                    var response = JsonSerializer.Deserialize<AgentDelegationResponse>(responseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (response is null)
                        return "Error: Invalid response from specialist agent";

                    if (response.Success)
                        return response.Result ?? "Task completed (no result)";
                    else
                        return $"Specialist agent error: {response.Error ?? "Unknown error"}";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading delegation response {RequestId}", requestId);
                    return $"Error reading specialist response: {ex.Message}";
                }
            }

            await Task.Delay(1000); // Poll every 1 second
        }

        _logger.LogWarning("Timeout waiting for delegation response {RequestId} (agent={AgentId})", requestId, agentId);
        return $"Timeout waiting for response from specialist agent '{agentId}' after {timeoutSeconds}s";
    }

    private async Task<string> ListAvailableAgents()
    {
        _logger.LogDebug("Tool 'list_available_agents' invoked [Group={Group}]", _groupName);

        var requestId = Guid.NewGuid().ToString();
        var message = new IpcMessage
        {
            Id = requestId,
            CorrelationId = _correlationId,
            Type = IpcMessageType.ListAvailableAgents,
            GroupName = _groupName,
            Payload = "{}"
        };

        var json = JsonSerializer.Serialize(message);
        var fileName = $"{message.Id}.json";
        var tempPath = Path.Combine(_ipcDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(_ipcDirectory, fileName);

        // Write request file
        _logger.LogDebug("Requesting list_available_agents {RequestId}", requestId);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);

        // Poll for response file: {requestId}.response.json
        var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(responseFile))
            {
                try
                {
                    _logger.LogDebug("Received list_available_agents response {RequestId}", requestId);
                    var responseText = await File.ReadAllTextAsync(responseFile);
                    File.Delete(responseFile); // Clean up
                    return responseText;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading list_available_agents response {RequestId}", requestId);
                    return $"Error reading response: {ex.Message}";
                }
            }

            await Task.Delay(100); // Poll every 100ms
        }

        _logger.LogWarning("Timeout waiting for list_available_agents response {RequestId}", requestId);
        return "Timeout waiting for agent list response from host";
    }
}
