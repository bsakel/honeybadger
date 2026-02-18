# Phase 5 — Multi-Agent Delegation (Tools, IPC Handlers, Routing)

## Goal

Wire up the actual multi-agent delegation flow. The main/router agent can now delegate tasks to specialist agents via the `delegate_to_agent` tool. Messages are routed through the agent registry, and each agent gets its configured tools and soul file.

**Prerequisite:** Phase 4 complete (AgentConfiguration, AgentRegistry, IPC types, AgentRequest fields all in place)

---

## Step 1 — AgentDelegationTools

**New file: `src/Honeybadger.Agent/Tools/AgentDelegationTools.cs`**

Two tools for inter-agent communication (only available to router agents):

### `delegate_to_agent(agentId, task, context?, timeoutSeconds?)`

- Writes `IpcMessageType.DelegateToAgent` IPC file with `DelegateToAgentPayload`
- Polls for `{requestId}.response.json` (same pattern as `ListTasks` in existing `IpcTools.cs`)
- Default timeout: 300 seconds, poll interval: 1 second
- Returns specialist's result text or error message

### `list_available_agents()`

- Writes `IpcMessageType.ListAvailableAgents` IPC file
- Polls for response (10s timeout, 100ms poll)
- Returns formatted agent list

Both tools reuse the same IPC write pattern as `IpcTools.WriteIpcFileAsync`:
```csharp
// Atomic write: temp + rename
await File.WriteAllTextAsync(tempPath, json);
File.Move(tempPath, finalPath, overwrite: true);
```

Constructor takes: `string ipcDirectory`, `string groupName`, `ILogger`, `string correlationId` — same pattern as `IpcTools`.

---

## Step 2 — AgentToolFactory

**New file: `src/Honeybadger.Host/Agents/AgentToolFactory.cs`**

Maps tool names from agent config → actual `AIFunction` instances. Filters tools by what the agent is allowed to use.

```csharp
public class AgentToolFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _ipcDirectory;

    public IEnumerable<AIFunction> CreateToolsForAgent(
        AgentConfiguration agentConfig, string groupName, string correlationId)
    {
        var ipcTools = new IpcTools(_ipcDirectory, groupName, ..., correlationId);
        AgentDelegationTools? delegationTools = agentConfig.IsRouter
            ? new AgentDelegationTools(_ipcDirectory, groupName, ..., correlationId)
            : null;

        foreach (var toolName in agentConfig.Tools)
        {
            var tool = toolName.ToLowerInvariant() switch
            {
                "send_message" => GetByName(ipcTools.GetAll(), "send_message"),
                "schedule_task" => GetByName(ipcTools.GetAll(), "schedule_task"),
                "list_tasks" => GetByName(ipcTools.GetAll(), "list_tasks"),
                "pause_task" => GetByName(ipcTools.GetAll(), "pause_task"),
                "resume_task" => GetByName(ipcTools.GetAll(), "resume_task"),
                "cancel_task" => GetByName(ipcTools.GetAll(), "cancel_task"),
                "delegate_to_agent" when delegationTools != null
                    => GetByName(delegationTools.GetAll(), "delegate_to_agent"),
                "list_available_agents" when delegationTools != null
                    => GetByName(delegationTools.GetAll(), "list_available_agents"),
                _ => null  // Log warning, skip unknown tools
            };
            if (tool != null) yield return tool;
        }
    }
}
```

Register as singleton in `Program.cs` with `ipcDir`:
```csharp
builder.Services.AddSingleton(sp =>
    new AgentToolFactory(sp.GetRequiredService<ILoggerFactory>(), ipcDir));
```

---

## Step 3 — IpcWatcherService Enhancement

**Edit: `src/Honeybadger.Host/Services/IpcWatcherService.cs`**

### 3A. Extend constructor

Current constructor (line 21-27):
```csharp
public class IpcWatcherService(
    IIpcTransport ipcTransport, IChatFrontend frontend,
    CronExpressionEvaluator cronEval, IServiceScopeFactory scopeFactory,
    string ipcDirectory, ILogger<IpcWatcherService> logger) : BackgroundService
```

Add new parameters:
```csharp
public class IpcWatcherService(
    IIpcTransport ipcTransport, IChatFrontend frontend,
    CronExpressionEvaluator cronEval, IServiceScopeFactory scopeFactory,
    AgentRegistry agentRegistry,        // NEW
    AgentToolFactory agentToolFactory,   // NEW
    ILoggerFactory loggerFactory,        // NEW (for creating specialist orchestrators)
    HoneybadgerOptions honeybadgerOptions, // NEW (for CLI endpoint)
    string ipcDirectory, ILogger<IpcWatcherService> logger) : BackgroundService
```

### 3B. Add switch cases (after `ListTasks` case, line 60)

```csharp
case IpcMessageType.DelegateToAgent:
    await HandleDelegateToAgentAsync(message, ct);
    break;
case IpcMessageType.ListAvailableAgents:
    await HandleListAvailableAgentsAsync(message, ct);
    break;
```

### 3C. HandleDelegateToAgentAsync

1. Deserialize `DelegateToAgentPayload` from `message.Payload`
2. Validate agent exists: `agentRegistry.TryGetAgent(payload.AgentId, out var agentConfig)`
3. If not found: write error response file
4. Load soul: `agentRegistry.LoadSoulFile(agentConfig.Soul)`
5. Build `AgentRequest` for specialist:
   - `AgentId`, `GroupName`, `Content = payload.Task`, `Model = agentConfig.Model`
   - `Soul`, `IsRouterAgent = false`, `AvailableTools = agentConfig.Tools`
   - `CopilotCliEndpoint` from `honeybadgerOptions.Agent.CopilotCli`
   - `GlobalMemory = payload.Context` (optional context from delegation)
6. Create tools via `agentToolFactory.CreateToolsForAgent(agentConfig, ...)`
7. Create `IpcTools` and `AgentOrchestrator`
8. Run: `orchestrator.RunAsync(request, onChunk: null, ct)`
9. Write response file: `{payload.RequestId}.response.json` with `AgentDelegationResponse`

### 3D. HandleListAvailableAgentsAsync

1. Call `agentRegistry.GetAgentSummary()`
2. Write to `{message.Id}.response.json`

### 3E. Helper methods

```csharp
private async Task WriteResponseFileAsync(string requestId, object response)
{
    var path = Path.Combine(ipcDirectory, $"{requestId}.response.json");
    var json = JsonSerializer.Serialize(response, JsonOpts);
    await File.WriteAllTextAsync(path, json);
}
```

---

## Step 4 — MessageLoopService Enhancement

**Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs`**

### 4A. Add constructor parameters

Add `AgentRegistry agentRegistry` and `AgentToolFactory agentToolFactory` to the primary constructor (line 21-28).

### 4B. Add DetermineAgentForMessage

```csharp
private AgentConfiguration? DetermineAgentForMessage(ChatMessage message)
{
    var router = agentRegistry.GetRouterAgent();
    if (router != null) return router;

    // Fallback to legacy single-agent mode (no agent configs)
    return null; // null means use existing behavior
}
```

### 4C. Modify ProcessMessageAsync (lines 89-101)

After building the base `AgentRequest`, check if we have an agent config:

```csharp
var agentConfig = DetermineAgentForMessage(message);

if (agentConfig != null)
{
    // Multi-agent mode
    var soul = agentRegistry.LoadSoulFile(agentConfig.Soul);
    request = request with
    {
        AgentId = agentConfig.AgentId,
        IsRouterAgent = agentConfig.IsRouter,
        Soul = soul,
        AvailableTools = agentConfig.Tools,
        Model = agentConfig.Model ?? request.Model
    };

    // If router, inject available agents summary into context
    if (agentConfig.IsRouter)
    {
        var agentSummary = agentRegistry.GetAgentSummary();
        request = request with
        {
            GlobalMemory = (request.GlobalMemory ?? "") + "\n\n" + agentSummary
        };
    }
}
```

The rest of `ProcessMessageAsync` is unchanged — it still calls `agentRunner.RunAgentAsync(request, ...)`.

---

## Step 5 — AgentOrchestrator Enhancement

**Edit: `src/Honeybadger.Agent/AgentOrchestrator.cs`**

### 5A. Update BuildSystemContext (lines 137-167)

Add soul file section and dynamic tool list:

```csharp
private static string BuildSystemContext(AgentRequest request)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Group: {request.GroupName}");

    // Soul file — highest priority
    if (!string.IsNullOrWhiteSpace(request.Soul))
    {
        sb.AppendLine();
        sb.AppendLine("## Your Identity");
        sb.AppendLine(request.Soul);
    }

    // Global context
    if (!string.IsNullOrWhiteSpace(request.GlobalMemory))
    {
        sb.AppendLine();
        sb.AppendLine("## Global Context");
        sb.AppendLine(request.GlobalMemory);
    }

    // Group context
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

    // Dynamic tool list (replaces hardcoded line 164)
    if (request.AvailableTools?.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine($"Available tools: {string.Join(", ", request.AvailableTools)}");
    }
    else
    {
        // Fallback for legacy mode
        sb.AppendLine();
        sb.AppendLine("You have tools: send_message, schedule_task, pause_task, resume_task, cancel_task, list_tasks.");
    }

    return sb.ToString();
}
```

### 5B. Consider tool injection for CreateNewSessionAsync

Currently `CreateNewSessionAsync` (line 125-135) always uses `_ipcTools.GetAll()`:
```csharp
Tools = [.. _ipcTools.GetAll()]
```

For multi-agent mode, we need the tools to come from `AgentToolFactory`. Two approaches:

**Option A (simpler, recommended):** Keep `AgentOrchestrator` constructor accepting `IpcTools`, but also accept an optional `IEnumerable<AIFunction>` for custom tools. When custom tools are provided, use those instead of `_ipcTools.GetAll()`.

**Option B:** Change `AgentOrchestrator` to accept `IEnumerable<AIFunction>` directly instead of `IpcTools`.

Go with Option B — it's cleaner and the `IpcTools` instance is already created by the caller.

Update constructor:
```csharp
public class AgentOrchestrator(IEnumerable<AIFunction> tools, ILogger<AgentOrchestrator> logger)
```

Update `CreateNewSessionAsync`:
```csharp
Tools = [.. tools]
```

Update `LocalAgentRunner` to pass `ipcTools.GetAll()`:
```csharp
var orchestrator = new AgentOrchestrator(ipcTools.GetAll(), logger);
```

---

## Step 6 — Program.cs Updates

**Edit: `src/Honeybadger.Console/Program.cs`**

### 6A. Register AgentToolFactory

```csharp
builder.Services.AddSingleton(sp =>
    new AgentToolFactory(sp.GetRequiredService<ILoggerFactory>(), ipcDir));
```

### 6B. Update IpcWatcherService factory (lines 104-110)

Add new parameters:
```csharp
builder.Services.AddHostedService(sp => new IpcWatcherService(
    sp.GetRequiredService<IIpcTransport>(),
    sp.GetRequiredService<IChatFrontend>(),
    sp.GetRequiredService<CronExpressionEvaluator>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<AgentRegistry>(),          // NEW
    sp.GetRequiredService<AgentToolFactory>(),        // NEW
    sp.GetRequiredService<ILoggerFactory>(),          // NEW
    sp.GetRequiredService<IOptions<HoneybadgerOptions>>().Value,  // NEW
    ipcDir,
    sp.GetRequiredService<ILogger<IpcWatcherService>>()));
```

---

## Files Summary

| Action | File |
|--------|------|
| New | `src/Honeybadger.Agent/Tools/AgentDelegationTools.cs` |
| New | `src/Honeybadger.Host/Agents/AgentToolFactory.cs` |
| Edit | `src/Honeybadger.Host/Services/IpcWatcherService.cs` — new constructor params, 2 new handlers |
| Edit | `src/Honeybadger.Host/Services/MessageLoopService.cs` — agent routing, tool factory |
| Edit | `src/Honeybadger.Agent/AgentOrchestrator.cs` — soul/dynamic tools, constructor change |
| Edit | `src/Honeybadger.Host/Agents/LocalAgentRunner.cs` — pass `ipcTools.GetAll()` to new constructor |
| Edit | `src/Honeybadger.Console/Program.cs` — register AgentToolFactory, update IpcWatcherService factory |
| New | `tests/Honeybadger.Integration.Tests/MultiAgentDelegationTests.cs` |

---

## Test Plan

### Automated Tests

**New file: `tests/Honeybadger.Integration.Tests/MultiAgentDelegationTests.cs`**

Test cases:
1. **AgentToolFactory** — verify router agent gets delegation tools, specialist does not
2. **AgentToolFactory** — verify unknown tool names are skipped with warning
3. **DelegateToAgent IPC flow** — write delegation IPC file, verify handler creates specialist request with correct soul/model/tools
4. **Legacy fallback** — verify system works when no agent configs exist (DetermineAgentForMessage returns null)
5. **BuildSystemContext with soul** — verify soul file appears as "## Your Identity" section

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass
```

### Manual Verification

```bash
# Start service
dotnet run --project src/Honeybadger.Console

# Expected startup logs:
# [INF] Registered agent: main (Main Agent) — Model: default, Tools: 3, MCP: 0
# [INF] Registered agent: scheduler (Task Scheduler) — Model: default, Tools: 6, MCP: 0
# [INF] Agent registry initialized with 2 agent(s)
```

```bash
# Connect chat client
dotnet run --project src/Honeybadger.Chat -- --group main

# Type: "Schedule a daily standup reminder at 9am on weekdays"
```

Expected flow in logs:
```
[INF] Selected agent main (router) for group main
[DBG] BuildSystemContext: ## Your Identity ... ## Available Specialist Agents ...
[DBG] Tool 'delegate_to_agent' invoked [AgentId=scheduler]
[INF] IPC DelegateToAgent from group main
[INF] Delegating to agent scheduler for group main
[INF] Agent scheduler completed delegation for group main
[INF] Message processing complete
```

Expected user experience:
- Main agent analyzes the request
- Main agent delegates to scheduler specialist
- Scheduler uses `schedule_task` tool
- Main agent synthesizes response
- User sees: "I've scheduled a daily standup reminder for 9am on weekdays."
