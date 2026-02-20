# Plan 11 — Agent & Pipeline Improvements

## Context

Honeybadger's agent instructions are minimal (souls are ~15 lines each) and the response pipeline lacks features found in mature assistant frameworks. Patterns from the NanoClaw project (same author) would improve agent behavior, UX, and robustness. This plan adds 7 patterns spanning agent instructions, response processing, scheduling, and message handling.

## Changes

### 1. `<internal>` tag stripping

Agents can wrap private reasoning in `<internal>...</internal>` tags. These are stripped before sending to the user but preserved in DB logs for debugging.

**Files:**
- `src/Honeybadger.Host/Services/MessageLoopService.cs` — In the `onChunk` callback (line ~169), strip `<internal>` content before calling `SendStreamChunkAsync`. Also strip from `streamedContent` before persisting and from non-streamed responses before `SendToUserAsync`.
- `src/Honeybadger.Core/Utilities/InternalTagStripper.cs` (**new**) — Static helper with regex: `<internal>[\s\S]*?</internal>`

**Soul file updates** — Add to `souls/main.md` and `souls/scheduler.md`:
```markdown
## Internal Reasoning
Wrap private reasoning, planning, or uncertainty in <internal> tags.
These are stripped before the user sees your response but logged for debugging.
Use them when you need to think through a problem before answering.
```

### 2. Memory usage guidance in soul files

Current soul files don't guide agents on when/what to memorize via `update_memory`.

**Files:**
- `souls/main.md` — Add section:
```markdown
## Memory
Use the update_memory tool to save:
- User preferences and recurring requests
- Important facts the user shares about themselves
- Decisions made during conversations that should persist
- Corrections to previous assumptions

Do NOT save: transient task details, conversation-specific context, or information already in CLAUDE.md.
```
- `souls/scheduler.md` — Add section:
```markdown
## Memory
Use the update_memory tool to save:
- User's preferred schedule patterns and timezone
- Recurring task configurations that get adjusted frequently
```

### 3. Task `context_mode` (group vs isolated)

Scheduled tasks can run with group conversation history (`group` mode, current default) or in a fresh isolated session (`isolated` mode — no history, just memory files and the task prompt).

**Files:**
- `src/Honeybadger.Data/Entities/ScheduledTaskEntity.cs` — Add `public string ContextMode { get; set; } = "group";`
- EF migration: `dotnet ef migrations add AddTaskContextMode --project src/Honeybadger.Data.Sqlite --startup-project src/Honeybadger.Console`
- `src/Honeybadger.Agent/Tools/Core/IpcTools.cs` — Add optional `contextMode` parameter to `ScheduleTask` (default `"group"`, accepts `"group"` or `"isolated"`)
- `src/Honeybadger.Host/Services/IpcWatcherService.cs` — Pass `ContextMode` through `ScheduleTaskPayload` to the entity
- `src/Honeybadger.Host/Services/SchedulerService.cs` — In `RunTaskAsync` (lines 110-142), check `task.ContextMode`: if `"isolated"`, skip conversation history loading and pass empty `ConversationHistory`
- `src/Honeybadger.Core/Models/` — Add `ContextMode` field to `ScheduleTaskPayload`

### 4. PreCompact-style transcript archival

Before conversation history is trimmed by token budget, archive the full conversation to a markdown file. This feeds Phase 7 (conversation summarization) later.

**Files:**
- `src/Honeybadger.Host/Services/MessageLoopService.cs` — After loading conversation history (line ~83), if the message count equals `ConversationHistoryCount` (i.e., we're at the limit and older messages will be lost), archive to `groups/{groupName}/transcripts/{date}.md`
- `src/Honeybadger.Host/Memory/TranscriptArchiver.cs` (**new**) — Static helper that formats messages as markdown and appends to the daily transcript file. Creates `transcripts/` directory if needed.

### 5. Typing indicator heartbeat

Current implementation sends a single `ThinkingShow` at start and `ThinkingHide` at end. For long-running agents, the typing indicator may time out on some frontends (Telegram's typing indicator expires after 5 seconds).

**Files:**
- `src/Honeybadger.Host/Services/MessageLoopService.cs` — In `ProcessMessageAsync`, replace the single `ShowAgentThinkingAsync` call with a background task that re-sends `ShowAgentThinkingAsync` every 4 seconds until cancelled. Cancel it when `HideAgentThinkingAsync` is called. Track last chunk timestamp; pause heartbeat if no chunks for 10+ seconds, resume when a chunk arrives.
- No interface changes — `IChatFrontend.ShowAgentThinkingAsync` already exists; we just call it periodically.

### 6. Follow-up message piping into running agents — DEFERRED

Currently, if a user sends a follow-up message while an agent is still processing, it queues behind the current message in `GroupQueue` and starts a new agent session. NanoClaw pipes follow-ups into the running session via IPC.

This is the most complex change and depends on the Copilot SDK supporting mid-session message injection. The Copilot SDK uses `session.SendAsync()` which may not support concurrent sends. **Deferred to a future plan** once the SDK's multi-turn capabilities are better understood. The GroupQueue serialization (one message at a time per group) is safe and correct for now.

### 7. Error response persistence

If `ProcessMessageAsync` fails after persisting the user message, the error message is sent to the user but not persisted to the DB. The conversation history then has a dangling user message with no response.

**Files:**
- `src/Honeybadger.Host/Services/MessageLoopService.cs` — In the catch block (line ~210), after sending the error message to the user, also persist an error response message to the DB so the conversation history reflects the failure.

## Files Changed

| File | Change |
|------|--------|
| `src/Honeybadger.Core/Utilities/InternalTagStripper.cs` | **New** — regex-based `<internal>` tag removal |
| `src/Honeybadger.Host/Services/MessageLoopService.cs` | Strip internal tags, typing heartbeat, error response persistence |
| `souls/main.md` | Add internal reasoning + memory guidance sections |
| `souls/scheduler.md` | Add internal reasoning + memory guidance sections |
| `src/Honeybadger.Data/Entities/ScheduledTaskEntity.cs` | Add `ContextMode` property |
| `src/Honeybadger.Data.Sqlite/` | EF migration for `ContextMode` column |
| `src/Honeybadger.Agent/Tools/Core/IpcTools.cs` | Add `contextMode` parameter to `schedule_task` |
| `src/Honeybadger.Core/Models/` | Add `ContextMode` to `ScheduleTaskPayload` |
| `src/Honeybadger.Host/Services/IpcWatcherService.cs` | Pass `ContextMode` through to entity |
| `src/Honeybadger.Host/Services/SchedulerService.cs` | Branch on `ContextMode` for history loading |
| `src/Honeybadger.Host/Memory/TranscriptArchiver.cs` | **New** — markdown transcript archival |

## Verification

```bash
dotnet build Honeybadger.slnx    # 0 errors, 0 warnings
dotnet test Honeybadger.slnx     # all existing + new tests pass
```

Manual testing:
- Send a message and verify `<internal>` content doesn't appear in chat output
- Schedule a task with `contextMode: "isolated"` and verify it runs without conversation history
- Watch typing indicator during a long response — should pulse every 4 seconds
- Trigger an agent error — verify error response appears in conversation history
