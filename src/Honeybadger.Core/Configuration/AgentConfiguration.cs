namespace Honeybadger.Core.Configuration;

/// <summary>
/// Configuration for a single agent in the multi-agent system.
/// Loaded from config/agents/*.json files at startup.
/// </summary>
public record AgentConfiguration
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Soul { get; init; }           // Path to soul file, relative to repo root
    public string? Model { get; init; }           // Model override (null = use default)
    public List<string> Tools { get; init; } = [];
    public List<string> McpServers { get; init; } = [];
    public List<string> Capabilities { get; init; } = [];
    public bool IsRouter { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}
