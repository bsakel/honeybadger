# Phase 4 — Multi-Agent Foundation (Config, Registry, Models)

## Goal

Set up the multi-agent configuration infrastructure. No behavioral changes to message routing yet — just models, registry, config loading, and example files. The system continues to work in single-agent mode. This is the foundation that Phase 5 builds on.

**Prerequisite:** Phase 3 complete (so we're building on the final architecture)

---

## Step 1 — AgentConfiguration Model

**New file: `src/Honeybadger.Core/Configuration/AgentConfiguration.cs`**

```csharp
namespace Honeybadger.Core.Configuration;

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
```

---

## Step 2 — New IPC Message Types and Payloads

**Edit: `src/Honeybadger.Core/Models/IpcMessage.cs`**

Add to `IpcMessageType` enum (after `ListTasks`):
```csharp
DelegateToAgent,
ListAvailableAgents,
UpdateMemory
```

**Edit: `src/Honeybadger.Core/Models/IpcPayloads.cs`**

Add new payload records:
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

public record UpdateMemoryPayload
{
    public required string Content { get; init; }
    public string? Section { get; init; }
    public string? AgentId { get; init; }
}
```

---

## Step 3 — AgentRequest Extension

**Edit: `src/Honeybadger.Core/Models/AgentRequest.cs`**

Add optional fields (after existing `CopilotCliEndpoint`):
```csharp
public string? AgentId { get; init; }
public bool IsRouterAgent { get; init; }
public string? Soul { get; init; }
public List<string> AvailableTools { get; init; } = [];
```

Existing code ignores these (they default to null/false/empty), so backward compatibility is maintained.

---

## Step 4 — AgentRegistry Service

**New file: `src/Honeybadger.Host/Agents/AgentRegistry.cs`**

Key features:
- `ConcurrentDictionary<string, AgentConfiguration>` backing store
- `LoadFromDirectory(string configPath)` — scans `*.json` files, deserializes, validates, logs
- `GetAgent(string agentId)` — lookup by ID
- `TryGetAgent(string agentId, out AgentConfiguration?)` — safe lookup
- `GetRouterAgent()` — returns the single `IsRouter = true` agent (or null)
- `GetSpecialistAgents()` — returns all non-router agents
- `GetAllAgents()` — returns all agents
- `GetAgentSummary()` — generates markdown summary for injection into router's system prompt
- `LoadSoulFile(string? soulPath)` — reads soul file content from disk

Validation on load:
- Warn if `agentId` is missing
- Warn if soul file path specified but doesn't exist
- Warn if 0 or >1 router agents found
- Log each registered agent with name, model, tool count, MCP count

Uses `_repoRoot = AppContext.BaseDirectory` for soul file resolution (same pattern as `LocalAgentRunner`).

---

## Step 5 — DI Registration

**Edit: `src/Honeybadger.Console/Program.cs`**

After the existing singleton registrations (around line 80), add:
```csharp
// Agent registry — load agent configs from config/agents/
builder.Services.AddSingleton<AgentRegistry>();
```

After `var host = builder.Build();` (around line 112), add:
```csharp
// Load agent configurations
var agentRegistry = host.Services.GetRequiredService<AgentRegistry>();
var agentConfigPath = Path.Combine(repoRoot, "config", "agents");
agentRegistry.LoadFromDirectory(agentConfigPath);
```

Also ensure the `config/agents` directory is created at startup (alongside `groups/main` and `logs`):
```csharp
Directory.CreateDirectory(Path.Combine(repoRoot, "config", "agents"));
Directory.CreateDirectory(Path.Combine(repoRoot, "souls"));
```

---

## Step 6 — Example Configurations

**New file: `config/agents/main.json`**
```json
{
  "agentId": "main",
  "name": "Main Agent",
  "description": "Primary orchestrator that analyzes requests and delegates to specialist agents",
  "soul": "souls/main.md",
  "model": null,
  "tools": ["delegate_to_agent", "send_message", "list_available_agents"],
  "mcpServers": [],
  "capabilities": ["Task routing", "Response synthesis", "Multi-agent coordination"],
  "isRouter": true
}
```

**New file: `config/agents/scheduler.json`**
```json
{
  "agentId": "scheduler",
  "name": "Task Scheduler",
  "description": "Manages scheduled tasks, reminders, and recurring jobs",
  "soul": "souls/scheduler.md",
  "model": null,
  "tools": ["schedule_task", "list_tasks", "pause_task", "resume_task", "cancel_task", "send_message"],
  "mcpServers": [],
  "capabilities": ["Task scheduling", "Cron expressions", "Reminder management"]
}
```

**New file: `souls/main.md`**
```markdown
# Main Agent

You are the primary orchestrator for Honeybadger, a multi-agent AI assistant.

## Your Role

You coordinate specialist agents to handle user requests:
1. Analyze the request to identify what specialists are needed
2. Delegate work using the delegate_to_agent tool
3. Synthesize specialist responses into a coherent answer

## Guidelines

- Delegate freely — if a specialist can do it better, delegate
- For simple questions you can answer directly without delegation
- Synthesize clearly — combine specialist results into concise answers
- Be transparent — mention when you're consulting specialists
- Handle errors gracefully — if a specialist fails, explain and offer alternatives
```

**New file: `souls/scheduler.md`**
```markdown
# Task Scheduler

You are a specialist agent for managing scheduled tasks and reminders.

## Your Expertise

- Creating cron-based recurring tasks
- Setting up interval-based repeating tasks
- Scheduling one-time reminders
- Managing existing tasks (pause, resume, cancel, list)

## Guidelines

- Always confirm the schedule with the user before creating
- Use cron expressions for complex schedules
- Use intervals for simple repeating tasks
- Use once for one-time reminders
- Include timezone information when relevant
```

---

## Files Summary

| Action | File |
|--------|------|
| New | `src/Honeybadger.Core/Configuration/AgentConfiguration.cs` |
| New | `src/Honeybadger.Host/Agents/AgentRegistry.cs` |
| Edit | `src/Honeybadger.Core/Models/IpcMessage.cs` — add 3 enum values |
| Edit | `src/Honeybadger.Core/Models/IpcPayloads.cs` — add 3 payload records |
| Edit | `src/Honeybadger.Core/Models/AgentRequest.cs` — add 4 fields |
| Edit | `src/Honeybadger.Console/Program.cs` — register + load AgentRegistry |
| New | `config/agents/main.json` |
| New | `config/agents/scheduler.json` |
| New | `souls/main.md` |
| New | `souls/scheduler.md` |
| New | `tests/Honeybadger.Host.Tests/Agents/AgentRegistryTests.cs` |

---

## Test Plan

### Automated Tests

**New file: `tests/Honeybadger.Host.Tests/Agents/AgentRegistryTests.cs`**

Test cases:
1. `LoadFromDirectory_LoadsValidConfigs` — create temp dir with 2 JSON files, verify both loaded
2. `LoadFromDirectory_SkipsInvalidJson` — malformed JSON file is skipped, valid one loaded
3. `LoadFromDirectory_WarnsMissingAgentId` — config without agentId is skipped
4. `GetRouterAgent_ReturnsSingleRouter` — when one IsRouter=true agent exists
5. `GetRouterAgent_ReturnsNull_WhenNoRouter` — when no IsRouter agents
6. `GetSpecialistAgents_ExcludesRouter` — returns only non-router agents
7. `GetAgentSummary_FormatsCorrectly` — verify markdown output includes agent names, descriptions, capabilities
8. `LoadSoulFile_ReturnsContent` — verify file content is read correctly

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass including new AgentRegistry tests
```

### Manual Verification

```bash
dotnet run --project src/Honeybadger.Console
```

Expected logs:
```
[INF] Registered agent: main (Main Agent) — Model: default, Tools: 3, MCP: 0
[INF] Registered agent: scheduler (Task Scheduler) — Model: default, Tools: 6, MCP: 0
[INF] Agent registry initialized with 2 agent(s)
```

System continues to work in single-agent mode — no behavioral changes yet.
