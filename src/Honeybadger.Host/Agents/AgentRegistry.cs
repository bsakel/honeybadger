using System.Collections.Concurrent;
using System.Text.Json;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Registry of all configured agents in the multi-agent system.
/// Loads agent configurations from config/agents/*.json at startup.
/// </summary>
public class AgentRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<string, AgentConfiguration> _agents = new();
    private readonly ILogger<AgentRegistry> _logger;
    private readonly string _repoRoot;

    public AgentRegistry(ILogger<AgentRegistry> logger)
    {
        _logger = logger;
        _repoRoot = Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Load all agent configurations from a directory containing *.json files.
    /// </summary>
    public void LoadFromDirectory(string configPath)
    {
        if (!Directory.Exists(configPath))
        {
            _logger.LogWarning("Agent config directory not found: {Path}", configPath);
            return;
        }

        var files = Directory.GetFiles(configPath, "*.json");
        if (files.Length == 0)
        {
            _logger.LogInformation("No agent configuration files found in {Path}", configPath);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<AgentConfiguration>(json, JsonOpts);

                if (config is null || string.IsNullOrWhiteSpace(config.AgentId))
                {
                    _logger.LogWarning("Invalid agent configuration in {File} (missing agentId)", file);
                    continue;
                }

                // Validate soul file exists if specified
                if (!string.IsNullOrWhiteSpace(config.Soul))
                {
                    var soulPath = Path.Combine(_repoRoot, config.Soul);
                    if (!File.Exists(soulPath))
                        _logger.LogWarning("Soul file not found for agent {AgentId}: {Path}", config.AgentId, soulPath);
                }

                _agents[config.AgentId] = config;

                _logger.LogInformation(
                    "Registered agent: {AgentId} ({Name}) — Model: {Model}, Tools: {ToolCount}, MCP: {McpCount}",
                    config.AgentId,
                    config.Name,
                    config.Model ?? "default",
                    config.Tools.Count,
                    config.McpServers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading agent configuration from {File}", file);
            }
        }

        // Validate router agent count
        var routers = _agents.Values.Count(a => a.IsRouter);
        if (routers == 0)
            _logger.LogWarning("No router agent configured (expected exactly 1 with IsRouter=true)");
        else if (routers > 1)
            _logger.LogWarning("Multiple router agents configured ({Count}) — expected exactly 1", routers);

        _logger.LogInformation("Agent registry initialized with {Count} agent(s)", _agents.Count);
    }

    /// <summary>
    /// Get an agent configuration by ID.
    /// </summary>
    public AgentConfiguration? GetAgent(string agentId)
    {
        _agents.TryGetValue(agentId, out var agent);
        return agent;
    }

    /// <summary>
    /// Try to get an agent configuration by ID.
    /// </summary>
    public bool TryGetAgent(string agentId, out AgentConfiguration? agent)
    {
        return _agents.TryGetValue(agentId, out agent);
    }

    /// <summary>
    /// Get the single router agent (IsRouter = true), or null if none configured.
    /// </summary>
    public AgentConfiguration? GetRouterAgent()
    {
        return _agents.Values.FirstOrDefault(a => a.IsRouter);
    }

    /// <summary>
    /// Get all specialist agents (IsRouter = false).
    /// </summary>
    public IEnumerable<AgentConfiguration> GetSpecialistAgents()
    {
        return _agents.Values.Where(a => !a.IsRouter);
    }

    /// <summary>
    /// Get all registered agents.
    /// </summary>
    public IEnumerable<AgentConfiguration> GetAllAgents()
    {
        return _agents.Values;
    }

    /// <summary>
    /// Generate a markdown summary of available agents for injection into router's system prompt.
    /// </summary>
    public string GetAgentSummary()
    {
        var specialists = GetSpecialistAgents().ToList();
        if (specialists.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Available Specialist Agents");
        sb.AppendLine();

        foreach (var agent in specialists)
        {
            sb.AppendLine($"### {agent.Name} (`{agent.AgentId}`)");
            sb.AppendLine(agent.Description);

            if (agent.Capabilities.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Capabilities:");
                foreach (var capability in agent.Capabilities)
                    sb.AppendLine($"- {capability}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Load soul file content from disk.
    /// </summary>
    public string? LoadSoulFile(string? soulPath)
    {
        if (string.IsNullOrWhiteSpace(soulPath))
            return null;

        var fullPath = Path.Combine(_repoRoot, soulPath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Soul file not found: {Path}", fullPath);
            return null;
        }

        _logger.LogDebug("Loading soul file: {Path}", fullPath);
        return File.ReadAllText(fullPath);
    }
}
