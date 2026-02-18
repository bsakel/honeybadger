# Phase 6 — Memory Enhancements

## Goal

Three memory improvements that build on the multi-agent foundation:
- **6A** Agents can write their own memory via `update_memory` IPC tool
- **6B** Separate persona/memory/summary files per group
- **6C** Token budget awareness for conversation history

**Prerequisite:** Phase 5 complete (multi-agent delegation working, agents have identity)

---

## 6A. `update_memory` IPC Tool

### Problem

Agents cannot persist learned facts. If a user says "remember that I prefer Python," the agent can mention it in a response but cannot save it anywhere.

### Agent-Side Tool

**Edit: `src/Honeybadger.Agent/Tools/IpcTools.cs`**

Add to `GetAll()`:
```csharp
AIFunctionFactory.Create(UpdateMemory, "update_memory",
    "Save a note to the group's persistent memory. Use this when the user asks you to remember something.")
```

New method:
```csharp
private async Task<string> UpdateMemory(
    [Description("The content to save to memory")]
    string content,
    [Description("Optional section name to organize the note under")]
    string? section = null)
{
    var payload = new UpdateMemoryPayload
    {
        Content = content,
        Section = section,
        AgentId = _agentId  // NEW field needed in IpcTools constructor
    };
    await WriteIpcFileAsync(IpcMessageType.UpdateMemory, payload);
    return "Memory updated";
}
```

Note: `IpcTools` constructor needs a new `string agentId` parameter (default empty for backward compatibility).

### Host-Side Handler

**Edit: `src/Honeybadger.Host/Services/IpcWatcherService.cs`**

Add case to switch:
```csharp
case IpcMessageType.UpdateMemory:
    await HandleUpdateMemoryAsync(message, ct);
    break;
```

Handler:
```csharp
private async Task HandleUpdateMemoryAsync(IpcMessage message, CancellationToken ct)
{
    var payload = JsonSerializer.Deserialize<UpdateMemoryPayload>(message.Payload, JsonOpts);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Content))
    {
        logger.LogWarning("Invalid UpdateMemory payload from {Group}", message.GroupName);
        return;
    }

    // Write to groups/{groupName}/MEMORY.md
    var memoryDir = Path.Combine(repoRoot, "groups", message.GroupName);
    Directory.CreateDirectory(memoryDir);
    var memoryPath = Path.Combine(memoryDir, "MEMORY.md");

    var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
    var agent = payload.AgentId ?? "unknown";
    var section = payload.Section ?? "Notes";

    var entry = $"\n## {section} ({agent}, {timestamp})\n- {payload.Content}\n";
    await File.AppendAllTextAsync(memoryPath, entry, ct);

    logger.LogInformation("Memory updated for group {Group} by {Agent}", message.GroupName, agent);
}
```

The `HierarchicalMemoryStore` cache (from Phase 1C) is automatically invalidated via `FileSystemWatcher` when `MEMORY.md` is written.

---

## 6B. Separate Persona / Memory / Summary Files

### Problem

A single `CLAUDE.md` per group conflates agent personality with learned facts with operational summaries.

### New Directory Structure

```
groups/{name}/
  CLAUDE.md     → persona (read-only, always loaded verbatim)
  MEMORY.md     → learned facts (agent-writable via update_memory)
  summary.md    → conversation summary (written by future Phase 7 summarization)
```

### HierarchicalMemoryStore Changes

**Edit: `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`**

Add two new methods:
```csharp
public string? LoadGroupAgentMemory(string groupName)
{
    var path = Path.Combine(_groupsRoot, groupName, "MEMORY.md");
    return _cache.GetOrAdd($"memory:{groupName}", _ =>
    {
        if (!File.Exists(path)) return null;
        logger.LogDebug("Loading group agent memory from {Path}", path);
        return File.ReadAllText(path);
    });
}

public string? LoadGroupSummary(string groupName)
{
    var path = Path.Combine(_groupsRoot, groupName, "summary.md");
    return _cache.GetOrAdd($"summary:{groupName}", _ =>
    {
        if (!File.Exists(path)) return null;
        logger.LogDebug("Loading group summary from {Path}", path);
        return File.ReadAllText(path);
    });
}
```

Update FileSystemWatcher (from Phase 1C) to also watch `MEMORY.md` and `summary.md`:
```csharp
_groupsWatcher = new FileSystemWatcher(_groupsRoot, "*.md") { IncludeSubdirectories = true };
```

### AgentRequest Extension

**Edit: `src/Honeybadger.Core/Models/AgentRequest.cs`**

Add:
```csharp
public string? AgentMemory { get; init; }    // From MEMORY.md
public string? ConversationSummary { get; init; } // From summary.md
```

### AgentOrchestrator BuildSystemContext Update

**Edit: `src/Honeybadger.Agent/AgentOrchestrator.cs`**

Add sections in `BuildSystemContext` after Group Context:

```csharp
if (!string.IsNullOrWhiteSpace(request.AgentMemory))
{
    sb.AppendLine();
    sb.AppendLine("## Remembered Facts");
    sb.AppendLine(request.AgentMemory);
}

if (!string.IsNullOrWhiteSpace(request.ConversationSummary))
{
    sb.AppendLine();
    sb.AppendLine("## Conversation Summary");
    sb.AppendLine(request.ConversationSummary);
}
```

### MessageLoopService Update

**Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs`**

When building `AgentRequest`, also load memory and summary:
```csharp
AgentMemory = memoryStore.LoadGroupAgentMemory(message.GroupName),
ConversationSummary = memoryStore.LoadGroupSummary(message.GroupName),
```

---

## 6C. Token Budget Awareness

### Problem

Fixed 20-message history ignores message sizes. 20 long messages could overflow the context window.

### Config

**Edit: `src/Honeybadger.Core/Configuration/AgentOptions.cs`**

Add:
```csharp
public int ConversationHistoryTokenBudget { get; set; } = 8000; // 0 = no limit
```

### Implementation

**Edit: `src/Honeybadger.Host/Formatting/ConversationFormatter.cs`** (created in Phase 1D)

Update the formatter to respect a token budget:
```csharp
public static string Format(IReadOnlyList<MessageEntity> messages, int tokenBudget = 0)
{
    var sb = new System.Text.StringBuilder();
    var estimatedTokens = 0;

    // Messages are ordered oldest-first; iterate in reverse (newest first) to prioritize recent
    foreach (var m in messages.Reverse())
    {
        var line = $"[{m.Sender}]: {m.Content}\n";
        var lineTokens = line.Length / 4; // Approximate: 4 chars per token

        if (tokenBudget > 0 && estimatedTokens + lineTokens > tokenBudget)
            break;

        estimatedTokens += lineTokens;
        sb.Insert(0, line); // Prepend to maintain chronological order
    }

    return sb.ToString();
}
```

### Wire Up

In `MessageLoopService`, pass the budget:
```csharp
var conversationHistory = ConversationFormatter.Format(recentMessages,
    opts.Agent.ConversationHistoryTokenBudget);
```

Same in `SchedulerService` if history is loaded.

---

## Files Summary

| Action | File |
|--------|------|
| Edit | `src/Honeybadger.Agent/Tools/IpcTools.cs` — add `update_memory` tool, `agentId` param |
| Edit | `src/Honeybadger.Host/Services/IpcWatcherService.cs` — add `HandleUpdateMemoryAsync` |
| Edit | `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs` — add `LoadGroupAgentMemory`, `LoadGroupSummary` |
| Edit | `src/Honeybadger.Core/Models/AgentRequest.cs` — add `AgentMemory`, `ConversationSummary` |
| Edit | `src/Honeybadger.Agent/AgentOrchestrator.cs` — add labeled sections |
| Edit | `src/Honeybadger.Host/Services/MessageLoopService.cs` — load memory/summary |
| Edit | `src/Honeybadger.Core/Configuration/AgentOptions.cs` — add `ConversationHistoryTokenBudget` |
| Edit | `src/Honeybadger.Host/Formatting/ConversationFormatter.cs` — token budget parameter |

---

## Test Plan

### Automated Tests

1. **update_memory tool** — verify IPC file is written with correct payload
2. **HandleUpdateMemoryAsync** — verify `MEMORY.md` is created/appended with attribution
3. **HierarchicalMemoryStore** — verify `LoadGroupAgentMemory` and `LoadGroupSummary` return correct content
4. **ConversationFormatter with budget** — verify messages are truncated when budget is exceeded
5. **ConversationFormatter with budget** — verify most recent messages are prioritized
6. **ConversationFormatter zero budget** — verify all messages included when budget is 0

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass
```

### Manual Verification

```bash
# In chat:
you> Remember that I prefer dark mode for all UIs

# Agent should call update_memory tool
# Check: groups/main/MEMORY.md contains the note with timestamp and agent ID

# Later:
you> What are my preferences?

# Agent should see the memory in context and recall the preference
```

Token budget test:
```bash
# Fill a group with 50 long messages
# Send a new message
# Check debug logs: conversation history should be truncated to ~8000 tokens
```
