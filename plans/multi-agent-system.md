# Multi-Agent Collaboration System

## Vision

Transform Honeybadger from a single-agent system into a **collaborative multi-agent system** where specialized agents work together to handle complex tasks. A main router agent receives user messages, decides which specialist agents should participate, delegates work to them, and synthesizes their responses.

**Key principles:**
- **Specialization through configuration** — Each agent defined by config file (soul, model, tools, MCP servers)
- **Dynamic discovery** — Agents registered at startup from `config/agents/*.json`
- **Hierarchical delegation** — Main agent orchestrates, specialists execute
- **Tool-based communication** — Agents use `delegate_to_agent` tool for inter-agent messaging
- **Copilot SDK first** — Focus on Copilot SDK, but keep architecture flexible for future backends

---

## Architecture Overview

### Current State (Single Agent)

```
User message
  ↓
MessageLoopService
  ↓
LocalAgentRunner
  ↓
AgentOrchestrator (single, monolithic)
  - Same tools for all messages
  - Same model (unless group override)
  - Same context structure
  ↓
User response
```

### Future State (Multi-Agent)

```
User message
  ↓
MessageLoopService
  ↓
AgentRouter (determines if this is for main agent or specialist)
  ↓
Main Agent (router/orchestrator)
  - Sees available specialist agents in system prompt
  - Has delegate_to_agent tool
  - Decides which specialists to involve
  ↓
Main agent calls: delegate_to_agent("researcher", "task description")
  ↓
IpcWatcherService receives DelegateToAgent IPC message
  ↓
AgentRegistry validates specialist exists
  ↓
Specialist AgentOrchestrator created
  - Specialist's soul file loaded
  - Specialist's model used
  - Specialist's tools only
  - Specialist's MCP servers
  ↓
Specialist executes and returns result
  ↓
Main agent receives specialist result
  ↓
Main agent synthesizes final response
  ↓
User sees result
```

---

## Comparison with OpenClaw and NanoClaw

| Feature | NanoClaw | OpenClaw | Honeybadger (Proposed) |
|---------|----------|----------|------------------------|
| Agent model | Group-based (WhatsApp groups) | Session-based entities | Config-file-based agents |
| Inter-agent communication | None (isolated) | Full (sessions_send, sessions_spawn) | Hierarchical (delegate_to_agent) |
| Agent configuration | Database + CLAUDE.md | Workspace directories | JSON config + soul files |
| Tool assignment | Same for all | Dynamic per-agent | Per-agent via config |
| MCP servers | One per run | Plugin-based | Per-agent via config, shared instances |
| Routing | WhatsApp group ID | Session key encodes agentId | Main agent decides |
| User visibility | Final output only | Subagent announcements | Configurable (final or intermediate) |
| Complexity | Low | High | Medium |

**Design choice:** Honeybadger takes a **middle path** — simpler than OpenClaw's session graph, but more flexible than NanoClaw's isolation.

---

## Agent Configuration Schema

### Directory Structure

```
config/
  agents/
    main.json          # Router/orchestrator agent
    researcher.json    # Web research specialist
    scheduler.json     # Task scheduling specialist
    coder.json         # Code analysis/generation
    analyst.json       # Data analysis

souls/
  main.md             # Main agent personality/instructions
  researcher.md       # Researcher personality
  scheduler.md
  coder.md
  analyst.md

mcp-servers/
  brave-search/       # MCP server for web search
  filesystem/         # MCP server for file operations
  database/           # MCP server for data queries
```

### Agent Configuration File Format

**`config/agents/researcher.json`:**
```json
{
  "agentId": "researcher",
  "name": "Research Specialist",
  "description": "Expert at web research, information gathering, and summarization",
  "soul": "souls/researcher.md",
  "model": "claude-sonnet-4.5",
  "tools": [
    "web_search",
    "read_file",
    "send_message",
    "summarize"
  ],
  "mcpServers": [
    "brave-search",
    "filesystem"
  ],
  "capabilities": [
    "Web research",
    "Document analysis",
    "Information synthesis",
    "Fact-checking"
  ],
  "maxTokens": 4000,
  "temperature": 0.7
}
```

**`config/agents/main.json`:**
```json
{
  "agentId": "main",
  "name": "Main Agent",
  "description": "Primary orchestrator that delegates to specialist agents",
  "soul": "souls/main.md",
  "model": "claude-opus-4.6",
  "tools": [
    "delegate_to_agent",
    "send_message",
    "list_available_agents"
  ],
  "mcpServers": [],
  "isRouter": true,
  "capabilities": [
    "Task routing",
    "Response synthesis",
    "Multi-agent coordination"
  ]
}
```

**`config/agents/scheduler.json`:**
```json
{
  "agentId": "scheduler",
  "name": "Task Scheduler",
  "description": "Manages scheduled tasks, reminders, and recurring jobs",
  "soul": "souls/scheduler.md",
  "model": "claude-sonnet-4.5",
  "tools": [
    "schedule_task",
    "list_tasks",
    "pause_task",
    "resume_task",
    "cancel_task",
    "send_message"
  ],
  "mcpServers": [],
  "capabilities": [
    "Task scheduling",
    "Cron expressions",
    "Reminder management",
    "Calendar operations"
  ]
}
```

### Soul File Format

**`souls/main.md`:**
```markdown
# Main Agent

You are the primary orchestrator for Honeybadger, a multi-agent AI assistant system.

## Your Role

You coordinate specialized agents to handle complex user requests. When a user asks for something:

1. **Analyze** the request to identify what specialists are needed
2. **Delegate** work to appropriate specialists using the delegate_to_agent tool
3. **Synthesize** their responses into a coherent answer for the user

## Available Specialists

You will see a list of available specialist agents in your context. Each has specific capabilities.

## Guidelines

- **Delegate freely** — If a specialist can do it better, delegate
- **Parallel work** — You can delegate to multiple agents for independent tasks
- **Synthesize clearly** — Combine specialist responses into clear, concise answers
- **Be transparent** — Let the user know when you're consulting specialists
- **Handle errors gracefully** — If a specialist fails, explain and offer alternatives

## Communication Style

- Professional but friendly
- Clear and concise
- Proactive in suggesting solutions
```

**`souls/researcher.md`:**
```markdown
# Research Specialist

You are an expert research agent specializing in information gathering and analysis.

## Your Expertise

- Web research using search tools
- Document analysis and summarization
- Fact-checking and verification
- Information synthesis from multiple sources

## Guidelines

- **Be thorough** — Search multiple sources when possible
- **Cite sources** — Always provide URLs or references
- **Be critical** — Evaluate source credibility
- **Summarize clearly** — Distill complex information into key points
- **Be current** — Prefer recent information when relevance matters

## Your Tools

- web_search: Find information on the web
- read_file: Analyze documents
- summarize: Create concise summaries
- send_message: Send updates to the user
```

---

## Core Components

### 1. AgentConfiguration Model

**New file: `src/Honeybadger.Core/Configuration/AgentConfiguration.cs`**

```csharp
namespace Honeybadger.Core.Configuration;

public record AgentConfiguration
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// Path to soul file (personality/instructions), relative to repo root.
    /// Example: "souls/researcher.md"
    /// </summary>
    public string? Soul { get; init; }

    /// <summary>
    /// Model override for this agent. If null, uses default from AgentOptions.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// List of tool names available to this agent.
    /// </summary>
    public List<string> Tools { get; init; } = new();

    /// <summary>
    /// List of MCP server names this agent needs.
    /// </summary>
    public List<string> McpServers { get; init; } = new();

    /// <summary>
    /// Human-readable capabilities for discovery/routing.
    /// </summary>
    public List<string> Capabilities { get; init; } = new();

    /// <summary>
    /// Whether this is the main router agent.
    /// </summary>
    public bool IsRouter { get; init; }

    /// <summary>
    /// Max tokens for this agent's responses.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Temperature (0.0-1.0) for this agent.
    /// </summary>
    public double? Temperature { get; init; }
}
```

---

### 2. AgentRegistry Service

**New file: `src/Honeybadger.Host/Agents/AgentRegistry.cs`**

```csharp
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Registry of all available agents loaded from config/agents/*.json.
/// Provides agent discovery, validation, and metadata generation for main agent.
/// </summary>
public class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentConfiguration> _agents = new();
    private readonly ILogger<AgentRegistry> _logger;
    private readonly string _repoRoot;

    public AgentRegistry(ILogger<AgentRegistry> logger)
    {
        _logger = logger;
        _repoRoot = AppContext.BaseDirectory;
    }

    /// <summary>
    /// Load all agent configurations from the specified directory.
    /// </summary>
    public void LoadFromDirectory(string configPath)
    {
        if (!Directory.Exists(configPath))
        {
            _logger.LogWarning("Agent config directory not found: {Path}. Creating it.", configPath);
            Directory.CreateDirectory(configPath);
            return;
        }

        var agentFiles = Directory.GetFiles(configPath, "*.json");

        if (agentFiles.Length == 0)
        {
            _logger.LogWarning("No agent configuration files found in {Path}", configPath);
            return;
        }

        foreach (var file in agentFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<AgentConfiguration>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (config is null)
                {
                    _logger.LogWarning("Failed to deserialize agent config: {File}", file);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(config.AgentId))
                {
                    _logger.LogWarning("Agent config missing agentId: {File}", file);
                    continue;
                }

                // Validate soul file exists if specified
                if (!string.IsNullOrEmpty(config.Soul))
                {
                    var soulPath = Path.Combine(_repoRoot, config.Soul);
                    if (!File.Exists(soulPath))
                    {
                        _logger.LogWarning("Soul file not found for agent {AgentId}: {Path}",
                            config.AgentId, soulPath);
                    }
                }

                _agents[config.AgentId] = config;
                _logger.LogInformation(
                    "Registered agent: {AgentId} ({Name}) — Model: {Model}, Tools: {ToolCount}, MCP: {McpCount}",
                    config.AgentId, config.Name, config.Model ?? "default",
                    config.Tools.Count, config.McpServers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load agent config from {File}", file);
            }
        }

        var routerCount = _agents.Values.Count(a => a.IsRouter);
        if (routerCount == 0)
        {
            _logger.LogWarning("No router agent found. You should have one agent with 'isRouter': true");
        }
        else if (routerCount > 1)
        {
            _logger.LogWarning("Multiple router agents found ({Count}). Only one should have 'isRouter': true",
                routerCount);
        }

        _logger.LogInformation("Agent registry initialized with {Count} agent(s)", _agents.Count);
    }

    /// <summary>
    /// Get agent configuration by ID.
    /// </summary>
    public AgentConfiguration? GetAgent(string agentId)
        => _agents.GetValueOrDefault(agentId);

    /// <summary>
    /// Try to get agent configuration by ID.
    /// </summary>
    public bool TryGetAgent(string agentId, out AgentConfiguration? config)
        => _agents.TryGetValue(agentId, out config);

    /// <summary>
    /// Get all registered agents.
    /// </summary>
    public IReadOnlyCollection<AgentConfiguration> GetAllAgents()
        => _agents.Values.ToList();

    /// <summary>
    /// Get the router agent (isRouter: true). Returns null if none or multiple found.
    /// </summary>
    public AgentConfiguration? GetRouterAgent()
    {
        var routers = _agents.Values.Where(a => a.IsRouter).ToList();
        return routers.Count == 1 ? routers[0] : null;
    }

    /// <summary>
    /// Get all specialist agents (isRouter: false).
    /// </summary>
    public IReadOnlyCollection<AgentConfiguration> GetSpecialistAgents()
        => _agents.Values.Where(a => !a.IsRouter).ToList();

    /// <summary>
    /// Generate a summary of available agents for injection into main agent's system prompt.
    /// </summary>
    public string GetAgentSummary()
    {
        var specialists = GetSpecialistAgents();

        if (!specialists.Any())
        {
            return "## Available Specialist Agents\n\n(None registered)";
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Available Specialist Agents");
        sb.AppendLine();

        foreach (var agent in specialists.OrderBy(a => a.AgentId))
        {
            sb.AppendLine($"### {agent.AgentId}");
            sb.AppendLine($"**Name:** {agent.Name}");
            sb.AppendLine($"**Description:** {agent.Description}");

            if (agent.Capabilities?.Any() == true)
            {
                sb.AppendLine($"**Capabilities:** {string.Join(", ", agent.Capabilities)}");
            }

            if (agent.Tools?.Any() == true)
            {
                sb.AppendLine($"**Tools:** {string.Join(", ", agent.Tools)}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Load soul file content for an agent. Returns empty string if not found.
    /// </summary>
    public string LoadSoulFile(string? soulPath)
    {
        if (string.IsNullOrEmpty(soulPath))
            return string.Empty;

        var fullPath = Path.Combine(_repoRoot, soulPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Soul file not found: {Path}", fullPath);
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read soul file: {Path}", fullPath);
            return string.Empty;
        }
    }
}
```

---

### 3. Agent Delegation Tool

**New file: `src/Honeybadger.Agent/Tools/AgentDelegationTools.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Honeybadger.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Agent.Tools;

/// <summary>
/// Tools for inter-agent communication and delegation.
/// Only available to router/main agents.
/// </summary>
public class AgentDelegationTools
{
    private readonly string _ipcDirectory;
    private readonly string _groupName;
    private readonly ILogger<AgentDelegationTools> _logger;
    private readonly string _correlationId;

    public AgentDelegationTools(
        string ipcDirectory,
        string groupName,
        ILogger<AgentDelegationTools> logger,
        string correlationId)
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
            "Delegate a task to a specialist agent. The specialist will process the task and return results."),
        AIFunctionFactory.Create(ListAvailableAgents, "list_available_agents",
            "List all available specialist agents and their capabilities."),
    ];

    private async Task<string> DelegateToAgent(
        [Description("The agent ID to delegate to (e.g., 'researcher', 'scheduler', 'coder')")]
        string agentId,
        [Description("The task or question for the specialist agent")]
        string task,
        [Description("Optional additional context or instructions for the specialist")]
        string? context = null,
        [Description("Maximum time to wait for response in seconds (default: 300)")]
        int timeoutSeconds = 300)
    {
        _logger.LogDebug("Tool 'delegate_to_agent' invoked [AgentId={AgentId}, Task={Task}]",
            agentId, task.Length > 50 ? task[..50] + "..." : task);

        var requestId = Guid.NewGuid().ToString();
        var payload = new DelegateToAgentPayload
        {
            RequestId = requestId,
            AgentId = agentId,
            Task = task,
            Context = context,
            TimeoutSeconds = timeoutSeconds
        };

        // Write delegation request
        await WriteIpcFileAsync(IpcMessageType.DelegateToAgent, payload, requestId);

        // Poll for response file: {requestId}.response.json
        var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        _logger.LogDebug("Waiting for delegation response [RequestId={RequestId}, Timeout={Timeout}s]",
            requestId, timeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(responseFile))
            {
                try
                {
                    _logger.LogDebug("Received delegation response [RequestId={RequestId}]", requestId);
                    var responseJson = await File.ReadAllTextAsync(responseFile);
                    File.Delete(responseFile); // Clean up

                    var response = JsonSerializer.Deserialize<AgentDelegationResponse>(responseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (response is null)
                    {
                        return "Error: Failed to parse agent response";
                    }

                    if (!response.Success)
                    {
                        _logger.LogWarning("Agent delegation failed [AgentId={AgentId}, Error={Error}]",
                            agentId, response.Error);
                        return $"Agent '{agentId}' encountered an error: {response.Error}";
                    }

                    return response.Result ?? "Agent completed task (no result provided)";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading delegation response [RequestId={RequestId}]", requestId);
                    return $"Error reading response from agent '{agentId}': {ex.Message}";
                }
            }

            await Task.Delay(1000); // Poll every second
        }

        _logger.LogWarning("Delegation timeout [RequestId={RequestId}, AgentId={AgentId}]",
            requestId, agentId);
        return $"Timeout waiting for response from agent '{agentId}' (waited {timeoutSeconds}s)";
    }

    private async Task<string> ListAvailableAgents()
    {
        _logger.LogDebug("Tool 'list_available_agents' invoked");

        var requestId = Guid.NewGuid().ToString();
        await WriteIpcFileAsync(IpcMessageType.ListAvailableAgents, new { }, requestId);

        // Poll for response
        var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(responseFile))
            {
                try
                {
                    var responseJson = await File.ReadAllTextAsync(responseFile);
                    File.Delete(responseFile);
                    return responseJson; // Already formatted by host
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading list_available_agents response");
                    return "Error reading agent list";
                }
            }

            await Task.Delay(100);
        }

        return "Timeout waiting for agent list";
    }

    private async Task WriteIpcFileAsync<T>(IpcMessageType type, T payload, string? requestId = null)
    {
        var message = new IpcMessage
        {
            Id = requestId ?? Guid.NewGuid().ToString(),
            CorrelationId = _correlationId,
            Type = type,
            GroupName = _groupName,
            Payload = JsonSerializer.Serialize(payload)
        };

        var json = JsonSerializer.Serialize(message);
        var fileName = $"{message.Id}.json";
        var tempPath = Path.Combine(_ipcDirectory, $"{fileName}.tmp");
        var finalPath = Path.Combine(_ipcDirectory, fileName);

        _logger.LogDebug("Writing IPC file {FileName} (type={Type})", fileName, type);

        // Atomic write: temp + rename
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);
    }
}
```

---

### 4. New IPC Message Types and Payloads

**Edit: `src/Honeybadger.Core/Models/IpcMessage.cs`**

Add new message types:
```csharp
public enum IpcMessageType
{
    SendMessage,
    ScheduleTask,
    PauseTask,
    ResumeTask,
    CancelTask,
    ListTasks,
    DelegateToAgent,        // NEW
    ListAvailableAgents     // NEW
}
```

**Edit: `src/Honeybadger.Core/Models/IpcPayloads.cs`**

Add new payloads:
```csharp
public record DelegateToAgentPayload
{
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public required string Task { get; init; }
    public string? Context { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
}

public record AgentDelegationResponse
{
    public bool Success { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
}
```

---

### 5. AgentRequest Enhancement

**Edit: `src/Honeybadger.Core/Models/AgentRequest.cs`**

Add fields:
```csharp
public record AgentRequest
{
    // ... existing fields ...

    /// <summary>
    /// Agent ID for this invocation (e.g., "main", "researcher").
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Whether this agent is the router/main agent.
    /// </summary>
    public bool IsRouterAgent { get; init; }

    /// <summary>
    /// Soul file content (personality/instructions).
    /// </summary>
    public string? Soul { get; init; }

    /// <summary>
    /// List of tool names available to this agent.
    /// </summary>
    public List<string> AvailableTools { get; init; } = new();
}
```

---

### 6. Tool Factory

**New file: `src/Honeybadger.Host/Agents/AgentToolFactory.cs`**

```csharp
using Honeybadger.Agent.Tools;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Creates tool sets for agents based on their configuration.
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
    /// Create tools for an agent based on its configuration.
    /// </summary>
    public IEnumerable<AIFunction> CreateToolsForAgent(
        AgentConfiguration agentConfig,
        string groupName,
        string correlationId)
    {
        var tools = new List<AIFunction>();

        // IPC tools (standard communication tools)
        var ipcTools = new IpcTools(_ipcDirectory, groupName,
            _loggerFactory.CreateLogger<IpcTools>(), correlationId);

        // Agent delegation tools (only for router agent)
        AgentDelegationTools? delegationTools = null;
        if (agentConfig.IsRouter)
        {
            delegationTools = new AgentDelegationTools(_ipcDirectory, groupName,
                _loggerFactory.CreateLogger<AgentDelegationTools>(), correlationId);
        }

        // Map tool names from config to actual tool instances
        foreach (var toolName in agentConfig.Tools)
        {
            var tool = toolName.ToLowerInvariant() switch
            {
                // IPC tools
                "send_message" => GetToolByName(ipcTools.GetAll(), "send_message"),
                "schedule_task" => GetToolByName(ipcTools.GetAll(), "schedule_task"),
                "pause_task" => GetToolByName(ipcTools.GetAll(), "pause_task"),
                "resume_task" => GetToolByName(ipcTools.GetAll(), "resume_task"),
                "cancel_task" => GetToolByName(ipcTools.GetAll(), "cancel_task"),
                "list_tasks" => GetToolByName(ipcTools.GetAll(), "list_tasks"),

                // Delegation tools (router only)
                "delegate_to_agent" when delegationTools != null
                    => GetToolByName(delegationTools.GetAll(), "delegate_to_agent"),
                "list_available_agents" when delegationTools != null
                    => GetToolByName(delegationTools.GetAll(), "list_available_agents"),

                // TODO: Add more tool types here (web_search, read_file, etc.)
                // These would come from MCP servers or custom tool implementations

                _ => null
            };

            if (tool != null)
            {
                tools.Add(tool);
            }
            else
            {
                // Log warning but don't fail — agent can work with partial tools
                _loggerFactory.CreateLogger<AgentToolFactory>()
                    .LogWarning("Unknown tool '{ToolName}' for agent {AgentId}",
                        toolName, agentConfig.AgentId);
            }
        }

        return tools;
    }

    private static AIFunction? GetToolByName(IEnumerable<AIFunction> tools, string name)
        => tools.FirstOrDefault(t =>
            t.Metadata.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
}
```

---

### 7. IpcWatcherService Enhancement

**Edit: `src/Honeybadger.Host/Services/IpcWatcherService.cs`**

Add handlers for new IPC message types:

```csharp
private async Task ProcessIpcMessage(IpcMessage message, CancellationToken ct)
{
    // ... existing handlers ...

    case IpcMessageType.DelegateToAgent:
        await HandleDelegateToAgent(message, ct);
        break;

    case IpcMessageType.ListAvailableAgents:
        await HandleListAvailableAgents(message, ct);
        break;
}

private async Task HandleDelegateToAgent(IpcMessage message, CancellationToken ct)
{
    var payload = JsonSerializer.Deserialize<DelegateToAgentPayload>(message.Payload);
    if (payload is null)
    {
        _logger.LogWarning("Invalid DelegateToAgent payload");
        await WriteErrorResponse(payload?.RequestId ?? message.Id, "Invalid payload");
        return;
    }

    // Validate agent exists
    if (!_agentRegistry.TryGetAgent(payload.AgentId, out var agentConfig))
    {
        _logger.LogWarning("Unknown agent requested: {AgentId}", payload.AgentId);
        await WriteErrorResponse(payload.RequestId, $"Unknown agent: {payload.AgentId}");
        return;
    }

    _logger.LogInformation("Delegating to agent {AgentId} for group {GroupName}",
        payload.AgentId, message.GroupName);

    try
    {
        // Build agent request for the specialist
        var soul = _agentRegistry.LoadSoulFile(agentConfig.Soul);
        var request = new AgentRequest
        {
            AgentId = agentConfig.AgentId,
            GroupName = message.GroupName,
            Content = payload.Task,
            Model = agentConfig.Model,
            CorrelationId = message.CorrelationId,
            IsRouterAgent = false,
            Soul = soul,
            AvailableTools = agentConfig.Tools,
            CopilotCliEndpoint = _copilotCliService.GetEndpoint(),
            // Optional: include context from delegation
            GlobalMemory = payload.Context
        };

        // Create specialist agent's tools
        var tools = _agentToolFactory.CreateToolsForAgent(agentConfig,
            message.GroupName, message.CorrelationId);

        // Create orchestrator for this specialist
        var ipcTools = tools.OfType<IpcTools>().FirstOrDefault()
            ?? new IpcTools(_ipcDirectory, message.GroupName,
                _loggerFactory.CreateLogger<IpcTools>(), message.CorrelationId);

        var orchestrator = new AgentOrchestrator(ipcTools,
            _loggerFactory.CreateLogger<AgentOrchestrator>());

        // Run the specialist agent
        var response = await orchestrator.RunAsync(request, onChunk: null, ct);

        // Write response back to requesting agent
        await WriteSuccessResponse(payload.RequestId, new AgentDelegationResponse
        {
            Success = response.Success,
            Result = response.Content,
            Error = response.Error
        });

        _logger.LogInformation("Agent {AgentId} completed delegation for group {GroupName}",
            payload.AgentId, message.GroupName);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during agent delegation to {AgentId}", payload.AgentId);
        await WriteErrorResponse(payload.RequestId, $"Delegation error: {ex.Message}");
    }
}

private async Task HandleListAvailableAgents(IpcMessage message, CancellationToken ct)
{
    _logger.LogDebug("Listing available agents");

    var summary = _agentRegistry.GetAgentSummary();
    await WriteResponseFile(message.Id, summary);
}

private async Task WriteSuccessResponse(string requestId, object response)
{
    var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
    var json = JsonSerializer.Serialize(response);
    await File.WriteAllTextAsync(responseFile, json);
}

private async Task WriteErrorResponse(string requestId, string error)
{
    await WriteSuccessResponse(requestId, new AgentDelegationResponse
    {
        Success = false,
        Error = error
    });
}

private async Task WriteResponseFile(string requestId, string content)
{
    var responseFile = Path.Combine(_ipcDirectory, $"{requestId}.response.json");
    await File.WriteAllTextAsync(responseFile, content);
}
```

---

### 8. MessageLoopService Enhancement

**Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs`**

Modify to determine if message should go to main/router agent:

```csharp
private async Task ProcessMessageAsync(ChatMessage message, CancellationToken ct)
{
    // ... existing code ...

    // Determine which agent to use
    var agentConfig = DetermineAgentForMessage(message);

    if (agentConfig is null)
    {
        _logger.LogWarning("No suitable agent found for message in group {GroupName}",
            message.GroupName);
        await _frontend.SendToUserAsync(new ChatMessage
        {
            GroupName = message.GroupName,
            Sender = "system",
            Content = "No agent available to handle this request.",
            IsFromAgent = true
        });
        return;
    }

    _logger.LogDebug("Selected agent {AgentId} for group {GroupName}",
        agentConfig.AgentId, message.GroupName);

    // Load agent's soul file
    var soul = _agentRegistry.LoadSoulFile(agentConfig.Soul);

    // Build agent request
    var request = new AgentRequest
    {
        AgentId = agentConfig.AgentId,
        GroupName = message.GroupName,
        Sender = message.Sender,
        Content = message.Content,
        Model = agentConfig.Model ?? group?.Model,
        CorrelationId = correlationId,
        IsRouterAgent = agentConfig.IsRouter,
        Soul = soul,
        AvailableTools = agentConfig.Tools,
        GlobalMemory = globalMemory,
        GroupMemory = groupMemory,
        ConversationHistory = conversationHistory,
        SessionId = sessionId,
        CopilotCliEndpoint = _copilotCliService.GetEndpoint()
    };

    // If router agent, add available agents to context
    if (agentConfig.IsRouter)
    {
        request = request with
        {
            GlobalMemory = globalMemory + "\n\n" + _agentRegistry.GetAgentSummary()
        };
    }

    // Create tools for this agent
    var tools = _agentToolFactory.CreateToolsForAgent(agentConfig,
        message.GroupName, correlationId);

    // ... rest of existing code ...
}

private AgentConfiguration? DetermineAgentForMessage(ChatMessage message)
{
    // For now, always use router agent if available, otherwise fall back to default
    // Future: could add routing logic based on message content, group settings, etc.

    var router = _agentRegistry.GetRouterAgent();
    if (router != null)
        return router;

    // Fallback: use first available agent
    var agents = _agentRegistry.GetAllAgents();
    return agents.FirstOrDefault();
}
```

---

### 9. AgentOrchestrator Enhancement

**Edit: `src/Honeybadger.Agent/AgentOrchestrator.cs`**

Update to use soul file and support router-specific context:

```csharp
private string BuildSystemContext(AgentRequest request)
{
    var sb = new StringBuilder();
    sb.AppendLine($"Group: {request.GroupName}");

    // Soul file (personality/instructions) — highest priority
    if (!string.IsNullOrWhiteSpace(request.Soul))
    {
        sb.AppendLine();
        sb.AppendLine("## Your Identity");
        sb.AppendLine(request.Soul);
    }

    // Global memory (project context)
    if (!string.IsNullOrWhiteSpace(request.GlobalMemory))
    {
        sb.AppendLine();
        sb.AppendLine("## Global Context");
        sb.AppendLine(request.GlobalMemory);
    }

    // Group memory (group-specific notes)
    if (!string.IsNullOrWhiteSpace(request.GroupMemory))
    {
        sb.AppendLine();
        sb.AppendLine("## Group Context");
        sb.AppendLine(request.GroupMemory);
    }

    // Conversation history
    if (!string.IsNullOrWhiteSpace(request.ConversationHistory))
    {
        sb.AppendLine();
        sb.AppendLine("## Recent Conversation");
        sb.AppendLine(request.ConversationHistory);
    }

    // Tools available to this agent
    if (request.AvailableTools?.Any() == true)
    {
        sb.AppendLine();
        sb.AppendLine($"Available tools: {string.Join(", ", request.AvailableTools)}");
    }

    return sb.ToString();
}
```

---

## Implementation Plan

### Phase 1: Foundation (Core Models & Registry)

**Goal:** Set up agent configuration schema and registry.

**Steps:**
1. Create `AgentConfiguration` model in `Honeybadger.Core`
2. Create `AgentRegistry` service in `Honeybadger.Host`
3. Add new IPC message types: `DelegateToAgent`, `ListAvailableAgents`
4. Add new payload models: `DelegateToAgentPayload`, `AgentDelegationResponse`
5. Update `AgentRequest` with new fields: `AgentId`, `IsRouterAgent`, `Soul`, `AvailableTools`

**Files to create:**
- `src/Honeybadger.Core/Configuration/AgentConfiguration.cs`
- `src/Honeybadger.Host/Agents/AgentRegistry.cs`

**Files to modify:**
- `src/Honeybadger.Core/Models/IpcMessage.cs` (add enum values)
- `src/Honeybadger.Core/Models/IpcPayloads.cs` (add payloads)
- `src/Honeybadger.Core/Models/AgentRequest.cs` (add fields)

**Verification:**
```bash
dotnet build Honeybadger.slnx  # 0 errors
dotnet test Honeybadger.slnx   # All tests pass
```

---

### Phase 2: Agent Delegation Tools

**Goal:** Create tools for inter-agent communication.

**Steps:**
1. Create `AgentDelegationTools` class with `delegate_to_agent` and `list_available_agents`
2. Create `AgentToolFactory` to build tool sets per agent configuration
3. Update `AgentOrchestrator` to use soul file and enhanced context building

**Files to create:**
- `src/Honeybadger.Agent/Tools/AgentDelegationTools.cs`
- `src/Honeybadger.Host/Agents/AgentToolFactory.cs`

**Files to modify:**
- `src/Honeybadger.Agent/AgentOrchestrator.cs` (enhance `BuildSystemContext`)

**Verification:**
```bash
dotnet build Honeybadger.slnx  # 0 errors
```

---

### Phase 3: IPC Delegation Handling

**Goal:** Enable host to process agent delegation requests.

**Steps:**
1. Add `HandleDelegateToAgent` to `IpcWatcherService`
2. Add `HandleListAvailableAgents` to `IpcWatcherService`
3. Add helper methods: `WriteSuccessResponse`, `WriteErrorResponse`, `WriteResponseFile`
4. Inject `AgentRegistry` and `AgentToolFactory` into `IpcWatcherService`

**Files to modify:**
- `src/Honeybadger.Host/Services/IpcWatcherService.cs`

**Verification:**
- Delegation IPC messages are processed correctly
- Response files are written with results or errors

---

### Phase 4: Message Routing

**Goal:** Route user messages to appropriate agents.

**Steps:**
1. Update `MessageLoopService.ProcessMessageAsync` to determine which agent to use
2. Implement `DetermineAgentForMessage` (start simple: always use router if available)
3. Load agent's soul file and inject into request
4. Build tools for agent using `AgentToolFactory`
5. If router agent, inject available agents summary into context

**Files to modify:**
- `src/Honeybadger.Host/Services/MessageLoopService.cs`

**Verification:**
- User messages routed to router agent
- Router agent sees available specialists in context

---

### Phase 5: Startup Integration

**Goal:** Load agents at application startup.

**Steps:**
1. Register `AgentRegistry` as singleton in DI container
2. Register `AgentToolFactory` as singleton
3. Load agents from `config/agents/` directory on startup
4. Validate at least one router agent exists
5. Log agent registration details

**Files to modify:**
- `src/Honeybadger.Console/Program.cs`

**Verification:**
```bash
dotnet run --project src/Honeybadger.Console

# Expected logs:
# [INF] Registered agent: main (Main Agent) — Model: claude-opus-4.6, Tools: 2, MCP: 0
# [INF] Registered agent: researcher (Research Specialist) — Model: claude-sonnet-4.5, Tools: 4, MCP: 2
# [INF] Agent registry initialized with 2 agent(s)
```

---

### Phase 6: Example Configurations

**Goal:** Provide working example agent configurations and soul files.

**Steps:**
1. Create `config/agents/main.json`
2. Create `config/agents/researcher.json`
3. Create `config/agents/scheduler.json`
4. Create `souls/main.md`
5. Create `souls/researcher.md`
6. Create `souls/scheduler.md`
7. Update `.gitignore` if needed (don't ignore souls or configs)

**Files to create:**
- `config/agents/main.json`
- `config/agents/researcher.json`
- `config/agents/scheduler.json`
- `souls/main.md`
- `souls/researcher.md`
- `souls/scheduler.md`

**Verification:**
- All config files are valid JSON
- All soul files exist and are readable
- Agent registry loads them successfully

---

### Phase 7: Testing & Documentation

**Goal:** Verify multi-agent system works end-to-end.

**Steps:**
1. Write unit tests for `AgentRegistry`
2. Write unit tests for `AgentToolFactory`
3. Write integration test for agent delegation flow
4. Update `CLAUDE.md` with multi-agent architecture details
5. Update `README.md` with example usage

**Files to create:**
- `tests/Honeybadger.Host.Tests/Agents/AgentRegistryTests.cs`
- `tests/Honeybadger.Host.Tests/Agents/AgentToolFactoryTests.cs`
- `tests/Honeybadger.Integration.Tests/MultiAgentDelegationTests.cs`

**Files to modify:**
- `CLAUDE.md` (add multi-agent section)
- `README.md` (add usage examples)

**Verification:**
```bash
dotnet test Honeybadger.slnx  # All tests pass (including new ones)
```

---

### Phase 8: End-to-End Scenario

**Goal:** Test real multi-agent conversation.

**Test scenario:**
```
User: "Research the latest React patterns from 2025 and schedule a reminder to review them tomorrow at 9am"

Expected flow:
1. Main agent receives message
2. Main agent sees available agents: researcher, scheduler
3. Main agent delegates: delegate_to_agent("researcher", "Research latest React patterns from 2025")
4. Researcher agent executes (uses web_search tool — stub for now)
5. Researcher returns results to main agent
6. Main agent delegates: delegate_to_agent("scheduler", "Schedule reminder for tomorrow 9am about reviewing React patterns")
7. Scheduler agent executes (uses schedule_task tool)
8. Scheduler returns confirmation to main agent
9. Main agent synthesizes: "I've researched React patterns and set your reminder."
10. User sees final response
```

**Manual test:**
```bash
dotnet run --project src/Honeybadger.Console

# In console:
you> Research the latest React patterns from 2025 and schedule a reminder to review them tomorrow at 9am

# Expected: Main agent delegates to specialists, synthesizes response
```

**Success criteria:**
- Main agent successfully delegates to specialists
- Specialists execute and return results
- Main agent synthesizes coherent response
- User sees final answer
- Logs show delegation flow

---

## Migration Strategy

### Backward Compatibility

**For existing installations:**

1. **Default single-agent mode** — If no agents are configured, system falls back to current behavior:
   - Use `AgentOptions.DefaultModel`
   - All tools available
   - No delegation

2. **Gradual adoption** — Users can:
   - Start with just a main agent (no specialists)
   - Add specialists one at a time
   - Test each specialist independently

3. **Configuration detection:**
```csharp
// In MessageLoopService
private AgentConfiguration? DetermineAgentForMessage(ChatMessage message)
{
    var router = _agentRegistry.GetRouterAgent();
    if (router != null)
        return router; // Multi-agent mode

    // Fallback to legacy single-agent mode
    return CreateLegacyAgentConfig();
}

private AgentConfiguration CreateLegacyAgentConfig()
{
    return new AgentConfiguration
    {
        AgentId = "default",
        Name = "Default Agent",
        Description = "Legacy single-agent mode",
        Model = _options.Agent.DefaultModel,
        Tools = ["send_message", "schedule_task", "list_tasks", "pause_task", "resume_task", "cancel_task"],
        McpServers = [],
        IsRouter = false
    };
}
```

---

## Future Enhancements (Not in Initial Plan)

### 1. MCP Server Integration

**Goal:** Actually start and connect to MCP servers based on agent configs.

**Implementation:**
- Create `McpServerManager` service
- Start MCP servers on demand (lazy load when agent needs them)
- Share MCP server instances across agents
- Pass MCP server endpoints to `CopilotClient` session config

---

### 2. Hot-Reload Agent Configs

**Goal:** Update agent configs without restarting.

**Implementation:**
- Add `FileSystemWatcher` to `AgentRegistry`
- Reload agent on config file change
- Notify active sessions to refresh context

---

### 3. Parallel Agent Delegation

**Goal:** Main agent can delegate to multiple specialists in parallel.

**Implementation:**
- Main agent calls `delegate_to_agent` multiple times
- Host processes delegations concurrently (up to `MaxConcurrentAgents`)
- Main agent receives results as they complete

---

### 4. Agent-to-Agent Visibility Control

**Goal:** Control which agents can see each other's results.

**Implementation:**
- Add `visibleTo` field in delegation response
- Filter conversation history based on agent permissions
- Privacy modes: `public`, `private`, `requester-only`

---

### 5. Advanced Routing

**Goal:** Auto-route messages to specialists based on content.

**Implementation:**
- Train a classifier or use LLM to analyze message
- Route directly to specialist if high confidence
- Fall back to main agent for ambiguous cases

---

### 6. Agent Metrics

**Goal:** Track agent performance and usage.

**Implementation:**
- Log delegation counts per agent
- Track average response time per agent
- Success/failure rates
- Token usage per agent

---

## Files Summary

### New Files (11)

| File | Purpose |
|------|---------|
| `src/Honeybadger.Core/Configuration/AgentConfiguration.cs` | Agent config model |
| `src/Honeybadger.Host/Agents/AgentRegistry.cs` | Agent discovery and metadata |
| `src/Honeybadger.Host/Agents/AgentToolFactory.cs` | Create tools per agent |
| `src/Honeybadger.Agent/Tools/AgentDelegationTools.cs` | Inter-agent communication tools |
| `config/agents/main.json` | Main router agent config |
| `config/agents/researcher.json` | Research specialist config |
| `config/agents/scheduler.json` | Scheduler specialist config |
| `souls/main.md` | Main agent personality |
| `souls/researcher.md` | Researcher personality |
| `souls/scheduler.md` | Scheduler personality |
| `tests/Honeybadger.Host.Tests/Agents/AgentRegistryTests.cs` | Unit tests |

### Modified Files (6)

| File | Changes |
|------|---------|
| `src/Honeybadger.Core/Models/IpcMessage.cs` | Add `DelegateToAgent`, `ListAvailableAgents` enum values |
| `src/Honeybadger.Core/Models/IpcPayloads.cs` | Add delegation payloads |
| `src/Honeybadger.Core/Models/AgentRequest.cs` | Add `AgentId`, `IsRouterAgent`, `Soul`, `AvailableTools` |
| `src/Honeybadger.Agent/AgentOrchestrator.cs` | Use soul file, enhanced context |
| `src/Honeybadger.Host/Services/IpcWatcherService.cs` | Handle delegation IPC messages |
| `src/Honeybadger.Host/Services/MessageLoopService.cs` | Route to appropriate agent |
| `src/Honeybadger.Console/Program.cs` | Register `AgentRegistry`, load agents |

---

## Success Criteria

1. ✅ **Agent registry loads configs** — Agents discovered from `config/agents/*.json`
2. ✅ **Main agent sees specialists** — Available agents injected in system prompt
3. ✅ **Delegation works** — Main agent can call `delegate_to_agent` successfully
4. ✅ **Specialists execute** — Specialist agents run with their own tools/soul/model
5. ✅ **Results return** — Delegation results flow back to main agent
6. ✅ **Synthesis works** — Main agent combines specialist results into coherent response
7. ✅ **Backward compatible** — System works with or without agent configs
8. ✅ **All tests pass** — No regressions, new tests for multi-agent features
9. ✅ **Documentation updated** — CLAUDE.md and README reflect new architecture

---

## Timeline Estimate

| Phase | Effort | Duration |
|-------|--------|----------|
| Phase 1: Foundation | Medium | 2-3 hours |
| Phase 2: Delegation Tools | Medium | 2-3 hours |
| Phase 3: IPC Handling | Small | 1-2 hours |
| Phase 4: Message Routing | Small | 1-2 hours |
| Phase 5: Startup Integration | Small | 1 hour |
| Phase 6: Example Configs | Small | 1 hour |
| Phase 7: Testing & Docs | Medium | 2-3 hours |
| Phase 8: E2E Scenario | Small | 1 hour |
| **Total** | | **11-17 hours** |

---

## Next Steps

1. **Review this plan** — Ensure alignment with vision
2. **Start Phase 1** — Build foundation (models, registry)
3. **Iterate** — Test each phase before moving to next
4. **Gather feedback** — Adjust based on actual usage

---

*Plan created: 2026-02-18*
*Target completion: TBD*
