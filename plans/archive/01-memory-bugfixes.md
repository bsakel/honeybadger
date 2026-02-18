# Phase 1 — Memory Bug Fixes and Quick Wins

## Goal

Fix the four immediate issues with the memory system. No architectural changes — purely correctness and performance fixes.

**Prerequisite:** None (first phase)

---

## 1A. Fix Global CLAUDE.md Content

**Problem:** `HierarchicalMemoryStore` (line 10) sets `_globalMemoryPath = Path.Combine(repoRoot, "CLAUDE.md")`. The repo-root `CLAUDE.md` is 600+ lines of developer documentation (architecture, build commands, migration guides). This is injected into every agent invocation as "Global Context" — wrong content.

**Fix:**

1. Create a new file `AGENT.md` at the repo root with actual agent persona/instructions:
   - Name, purpose, communication style
   - Key capabilities and guidelines
   - Compact (under 50 lines)

2. Change `HierarchicalMemoryStore` constructor:
   ```
   // File: src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs, line 10
   // Change: Path.Combine(repoRoot, "CLAUDE.md")
   // To:     Path.Combine(repoRoot, "AGENT.md")
   ```

3. Expand `groups/main/CLAUDE.md` from current 3-line stub into meaningful per-group context.

**Files:**
- Edit: `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs` — change `_globalMemoryPath`
- New: `AGENT.md` at repo root
- Edit: `groups/main/CLAUDE.md` — expand content

---

## 1B. Fix Double-Injection of Current Message

**Problem:** In `MessageLoopService.ProcessMessageAsync`:
- Line 64: `await msgRepo.AddMessageAsync(...)` — persists user message
- Line 69: `await msgRepo.GetRecentMessagesAsync(...)` — loads history (includes just-persisted message)
- The agent sees the current message in `## Recent Conversation` AND again via `session.SendAsync(prompt)`

**Fix:** Swap the order — load history BEFORE persisting. Move lines 68-71 to before line 64:

```csharp
// BEFORE persisting — load recent conversation history
var opts = options.Value;
var recentMessages = await msgRepo.GetRecentMessagesAsync(message.GroupName, opts.Agent.ConversationHistoryCount, ct);
var conversationHistory = FormatConversationHistory(recentMessages);
logger.LogDebug("Loaded {Count} recent messages", recentMessages.Count);

// THEN persist the user's message
await msgRepo.AddMessageAsync(message.GroupName, message.Id, message.Sender, message.Content, false, ct);
logger.LogDebug("Persisted user message");
```

**Files:**
- Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs` — reorder lines 64-71

---

## 1C. Cache Memory Files with FileSystemWatcher

**Problem:** `HierarchicalMemoryStore` calls `File.ReadAllText()` on every single message. No caching — both `LoadGlobalMemory()` and `LoadGroupMemory()` hit disk every time.

**Fix:** Add in-memory cache with `FileSystemWatcher` invalidation. The watcher pattern is already established in the codebase — see `src/Honeybadger.Host/Ipc/FileBasedIpcTransport.cs`.

Add to `HierarchicalMemoryStore`:
```csharp
private readonly ConcurrentDictionary<string, string?> _cache = new();
private readonly FileSystemWatcher? _globalWatcher;
private readonly FileSystemWatcher? _groupsWatcher;
```

`LoadGlobalMemory()` and `LoadGroupMemory()` check cache first:
```csharp
public string? LoadGlobalMemory()
{
    return _cache.GetOrAdd("global", _ =>
    {
        if (!File.Exists(_globalMemoryPath)) return null;
        logger.LogDebug("Loading global memory from {Path} (cache miss)", _globalMemoryPath);
        return File.ReadAllText(_globalMemoryPath);
    });
}
```

FileSystemWatcher invalidates on `Changed`/`Created`/`Deleted`:
```csharp
_globalWatcher = new FileSystemWatcher(Path.GetDirectoryName(_globalMemoryPath)!, Path.GetFileName(_globalMemoryPath));
_globalWatcher.Changed += (_, _) => _cache.TryRemove("global", out _);
_globalWatcher.EnableRaisingEvents = true;

_groupsWatcher = new FileSystemWatcher(_groupsRoot, "CLAUDE.md") { IncludeSubdirectories = true };
_groupsWatcher.Changed += (_, e) => InvalidateGroupCache(e.FullPath);
_groupsWatcher.EnableRaisingEvents = true;
```

**Files:**
- Edit: `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs` — add caching + watchers

---

## 1D. Memory Context for Scheduled Tasks

**Problem:** `SchedulerService.RunTaskAsync` (lines 109-119) builds `AgentRequest` with `GlobalMemory` and `GroupMemory` already populated, but sets NO `ConversationHistory` and NO `SessionId`. Scheduled tasks have zero conversational continuity.

**Fix (3 parts):**

### 1D-i. Extract `FormatConversationHistory` to shared utility

Currently `private static` in `MessageLoopService` (line 172). Extract to a shared location so `SchedulerService` can reuse it:

```csharp
// New file or internal static class in Honeybadger.Host
internal static class ConversationFormatter
{
    public static string Format(IReadOnlyList<MessageEntity> messages)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in messages)
            sb.AppendLine($"[{m.Sender}]: {m.Content}");
        return sb.ToString();
    }
}
```

Update `MessageLoopService` line 70 to call `ConversationFormatter.Format(recentMessages)`.

### 1D-ii. Add `ScheduledTaskHistoryCount` config

```csharp
// File: src/Honeybadger.Core/Configuration/AgentOptions.cs
// Add after line 10 (ConversationHistoryCount):
public int ScheduledTaskHistoryCount { get; set; } = 0; // 0 = disabled
```

### 1D-iii. Load history in `SchedulerService.RunTaskAsync`

After building the base `AgentRequest` (line 119), optionally add conversation history:

```csharp
// After line 119 in SchedulerService.cs
var historyCount = agentOptions.Value.ScheduledTaskHistoryCount;
if (historyCount > 0)
{
    var msgRepo = scope.ServiceProvider.GetRequiredService<MessageRepository>();
    var recentMessages = await msgRepo.GetRecentMessagesAsync(task.GroupName, historyCount, ct);
    request = request with { ConversationHistory = ConversationFormatter.Format(recentMessages) };
}
```

Note: `SchedulerService` already has a scoped `TaskRepository` via `scopeFactory.CreateScope()` (line 64). Adding `MessageRepository` follows the same pattern.

**Files:**
- New: `src/Honeybadger.Host/Formatting/ConversationFormatter.cs` (or similar location)
- Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs` — call shared formatter
- Edit: `src/Honeybadger.Host/Services/SchedulerService.cs` — optional history loading
- Edit: `src/Honeybadger.Core/Configuration/AgentOptions.cs` — add `ScheduledTaskHistoryCount`

---

## Test Plan

### Automated Tests

Write tests for:
1. **HierarchicalMemoryStore cache** — verify second call returns cached content, verify invalidation on file change
2. **Double-injection fix** — verify history does not include the current message being processed
3. **ConversationFormatter** — verify formatting output
4. **SchedulerService with history** — verify `ConversationHistory` is populated when `ScheduledTaskHistoryCount > 0`

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # 36 existing + 4-6 new tests pass
```

### Manual Verification

1. **AGENT.md content**: Run app, send a message. Check `logs/honeybadger-debug.log` for the system prompt — it should NOT contain architecture docs, migration commands, or test counts.

2. **No double-injection**: Send "hello world". Check debug log for `## Recent Conversation` section — "hello world" should NOT appear in the history block (it's the current message being sent via `session.SendAsync`).

3. **Cache hits**: Send 3 messages quickly. Check debug logs — should see "cache miss" on first call, then no disk read logs for subsequent calls.

4. **Scheduler memory**: Schedule a task via agent. When it fires, check debug logs for `ConversationHistory` field in the `AgentRequest` (if `ScheduledTaskHistoryCount` is set > 0 in `appsettings.json`).
