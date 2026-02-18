# Memory System Analysis: Honeybadger vs. NanoClaw vs. OpenClaw

## Purpose

Comparative analysis of the memory management systems across three projects, followed by a prioritized list of enhancements for Honeybadger. "Memory" means context memory, long-term memory, and conversation continuity — not RAM.

---

## 1. How Each Project Handles Memory

### 1A. Honeybadger (current)

**Layers:**

| Layer | Mechanism | Detail |
|-------|-----------|--------|
| Short-term | Rolling 20-message window from SQLite | Fetched by `MessageRepository.GetRecentMessagesAsync`, formatted as plain `[sender]: content` lines, injected into `## Recent Conversation` block of system prompt |
| Long-term | Two static Markdown files read from disk | `CLAUDE.md` (repo root → GlobalMemory) + `groups/{name}/CLAUDE.md` (GroupMemory), read fresh on every message via `HierarchicalMemoryStore` |
| Session | `SessionId` stored in DB but **never used** | Copilot SDK can't resume sessions with tools, so every invocation creates a new session. Continuity is faked via the conversation history text block. |

**System prompt structure** (built in `AgentOrchestrator.BuildSystemContext`):
```
Group: {groupName}

## Global Context
{raw text of CLAUDE.md}

## Group Context
{raw text of groups/{name}/CLAUDE.md}

## Recent Conversation
[user]: ...
[agent]: ...

You have tools: send_message, schedule_task, ...
```

**Known issues:**
- The global `CLAUDE.md` is currently the full project developer docs (~600 lines), not a personality/agent instructions file — wrong content injected into every context
- Current user message is persisted to DB *before* history is loaded, so the agent sees it **twice** (in the history block and again via `session.SendAsync`)
- No caching — `HierarchicalMemoryStore` does a `File.ReadAllText()` on every single message
- Agents have no tool to write to their own memory files
- No summarization — when a group exceeds 20 messages, old context is hard-dropped
- Scheduled tasks receive no conversation history or memory context at all

**Key files:**
- `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`
- `src/Honeybadger.Host/Services/MessageLoopService.cs`
- `src/Honeybadger.Agent/AgentOrchestrator.cs` (`BuildSystemContext` method)
- `src/Honeybadger.Data/Repositories/MessageRepository.cs`
- `src/Honeybadger.Core/Models/AgentRequest.cs` (fields: `GlobalMemory`, `GroupMemory`, `ConversationHistory`)

---

### 1B. NanoClaw

**Layers:**

| Layer | Mechanism | Detail |
|-------|-----------|--------|
| Short-term | Message cursor system | Fetches all human messages since the agent's last-processed timestamp. Formatted as XML: `<message sender="..." time="...">content</message>`. Cursor persisted to `router_state` SQLite table. |
| Long-term | CLAUDE.md files (agent-writable) | Global (`groups/global/CLAUDE.md`) + per-group (`groups/{name}/CLAUDE.md`) + arbitrary `.md` notes in group folder. Agents write these using Claude Code's native file edit tools. |
| Session transcripts | JSONL files | Native Claude Code session files. A `sessions-index.json` tracks them with summaries. Formatted as Markdown archives for context. |
| Session continuity | SDK session resumption | Session IDs persisted per group and actually resumed. Agent has native memory of prior conversations through the SDK. |
| Task context modes | `context_mode` per task | `'group'` = inherits active session ID (has historical context); `'isolated'` = runs sessionless (clean slate). |
| Situational snapshots | Pre-invocation JSON files | `tasks.json` + `groups.json` written to IPC dir before each run. Agent reads them with file tools. |

**Key differentiators vs. Honeybadger:**
- Agents can write their own memory (CLAUDE.md files via Claude Code's native file tools)
- Session resumption actually works (no tools-in-session limitation because Claude Code manages its own session natively)
- Message cursor system captures all messages since last agent interaction — more complete than a fixed-N window
- Tasks can opt in or out of historical conversation context via `context_mode`

---

### 1C. OpenClaw (most sophisticated)

**Layers:**

| Layer | Mechanism | Detail |
|-------|-----------|--------|
| Short-term | Full JSONL conversation history | Retained indefinitely; compacted when context window budget is exceeded |
| Compaction | Multi-strategy LLM summarization | Token-budget pruning → chunked LLM summarization → stage-based summarization → fallback filtering. Repairs orphaned tool_use/tool_result pairs after pruning. |
| Long-term (files) | `MEMORY.md` + `memory/*.md` | Agent-writable via bash tools. Watched by chokidar; re-indexed on change with 5s debounce. |
| Long-term (index) | SQLite vector DB + FTS5 | `chunks` table with embeddings, `chunks_vec` (sqlite-vec for ANN), `chunks_fts` (BM25). Session JSONL files are also indexed — past conversations are semantically searchable. |
| Retrieval | Tool-driven: `memory_search` + `memory_get` | Agent calls these on demand when it needs context. Not pre-injected. |
| Search pipeline | Hybrid vector + BM25 + MMR + temporal decay | Cosine similarity merged with BM25, re-ranked by Maximal Marginal Relevance to diversify results, optionally decayed by age. |
| Temporal decay | Exponential decay on scores | `e^(-lambda * age)`. Dated files (`memory/YYYY-MM-DD.md`) decay; `MEMORY.md` and non-dated files are evergreen-exempt. |
| Context files | Verbatim embedding | `MEMORY.md`, `SOUL.md`, `USER.md`, `AGENTS.md` embedded verbatim in system prompt when present. |

**Key differentiators:**
- Retrieval is agent-driven and lazy (agent calls `memory_search` when it decides it needs context), not blindly pre-injected
- Past conversation turns are semantically searchable — not just recent N messages
- Temporal decay distinguishes timeless facts from dated notes
- Context window managed dynamically with summarization fallbacks
- Embedding provider is configurable (OpenAI, Gemini, Voyage, local llama.cpp)

---

## 2. Comparison Table

| Feature | Honeybadger | NanoClaw | OpenClaw |
|---------|-------------|----------|----------|
| Short-term context | Last 20 msgs (fixed N, flat text) | All msgs since cursor (XML-tagged) | Full history, compacted to token budget |
| Context overflow handling | Hard drop (oldest msgs lost) | ❌ none | Chunked LLM summarization |
| Long-term memory store | 2 static Markdown files (read-only to agent) | CLAUDE.md files (agent-writable via Claude Code tools) | MEMORY.md + memory/*.md (agent-writable) + SQLite vector index |
| Session continuity | Text injection only (session never resumed) | ✅ Native SDK session resumption | ✅ Full JSONL history |
| Semantic search | ❌ | ❌ | ✅ Hybrid vector+BM25+MMR+decay |
| Agent-writable memory | ❌ | ✅ (Claude Code file tools) | ✅ (bash tools + watcher + re-index) |
| Memory for scheduled tasks | ❌ (tasks run blind) | ✅ (context_mode: group\|isolated) | ✅ |
| Temporal decay | ❌ | ❌ | ✅ |
| Past-session search | ❌ | JSONL archives (file read only) | ✅ (JSONL indexed in vector store) |
| Token budget awareness | ❌ | ❌ | ✅ |
| Double-injection bug | ✅ (has it) | ❌ | ❌ |
| Memory file caching | ❌ (disk read every msg) | ❌ | ✅ (file watcher + debounce) |

---

## 3. Proposed Enhancements for Honeybadger

Ordered by effort/impact. Each is independent and can be done incrementally.

---

### Enhancement 1 — Fix global CLAUDE.md content *(Trivial, immediate)*

**Problem:** The repo-root `CLAUDE.md` is the full developer documentation (~600 lines) and is injected into every agent context as "Global Context." The agent receives architectural notes, migration commands, and test counts — not a useful AI persona or instructions.

**Fix:** Create a separate `AGENT.md` at the repo root (or `groups/global/CLAUDE.md`) as the actual agent persona/instruction file. Update `HierarchicalMemoryStore.LoadGlobalMemory()` to point at the right file. Expand `groups/main/CLAUDE.md` from the current 3-line stub into meaningful per-group context.

**Files:** `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`, `groups/main/CLAUDE.md`, new `AGENT.md`

---

### Enhancement 2 — Fix the double-injection of current message *(Small bug fix)*

**Problem:** In `MessageLoopService.ProcessMessageAsync`, the current user message is persisted to DB at line 64, then `GetRecentMessagesAsync` is called at line 69 — which includes the just-persisted message. The agent sees it in `## Recent Conversation` AND receives it again via `session.SendAsync`.

**Fix:** Either fetch history before persisting the current message, query for messages strictly older than the current message ID, or exclude `IsFromAgent=false` messages with the current `ExternalId`.

**Files:** `src/Honeybadger.Host/Services/MessageLoopService.cs`

---

### Enhancement 3 — Cache memory files with FileSystemWatcher *(Small, high value)*

**Problem:** `HierarchicalMemoryStore` calls `File.ReadAllText()` on every single message. The global file can be large and is re-read unnecessarily on every request.

**Fix:** Cache loaded content in-memory per path. Invalidate via `FileSystemWatcher` on the `groups/` directory and repo root. This matches the openclaw pattern (chokidar watcher).

**Files:** `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`

---

### Enhancement 4 — Memory context for scheduled tasks *(Small, high value)*

**Problem:** When `SchedulerService` fires a task, the `AgentRequest` has no `GlobalMemory`, `GroupMemory`, or `ConversationHistory`. The scheduled agent runs with zero context about who it's serving or what's been discussed.

**Fix:** In the `AgentRequest` construction for scheduled tasks, load memory the same way `MessageLoopService` does: call `HierarchicalMemoryStore.LoadGlobalMemory()` and `LoadGroupMemory(groupName)`. Optionally load recent conversation history for the group (configurable: on/off, how many messages).

**Files:** `src/Honeybadger.Host/Services/SchedulerService.cs`

---

### Enhancement 5 — `update_memory` IPC tool for agents *(Medium, high value)*

**Problem:** Agents cannot write to their own memory. If a user says "remember that I prefer Python," the agent can include this in a response but cannot persist it anywhere.

**Fix:** Add an `update_memory` IPC tool alongside the existing tools (`send_message`, `schedule_task`, etc.). The agent calls it with content to append. The host validates the target path (same security model as mounts) and appends to `groups/{groupName}/CLAUDE.md` under an `## Agent Notes` section (or creates it).

**Protocol:**
```json
{ "type": "update_memory", "groupName": "main", "content": "User prefers Python for scripting." }
```

**Files:** `src/Honeybadger.Agent/Tools/IpcTools.cs`, `src/Honeybadger.Host/Services/IpcWatcherService.cs`, `src/Honeybadger.Core/Models/IpcMessage.cs` (new `IpcMessageType.UpdateMemory`)

---

### Enhancement 6 — Separate persona / memory / summary files *(Medium, architectural)*

**Problem:** A single `CLAUDE.md` per group conflates agent personality with learned facts with operational summaries. This makes it hard to update any one of them independently and hard to eventually add semantic indexing to just the facts.

**Fix:** (OpenClaw-inspired) Standardize the per-group directory structure:
```
groups/{name}/
  CLAUDE.md     → agent persona/identity (always loaded verbatim, never auto-modified)
  MEMORY.md     → learned facts and user preferences (agent-writable via update_memory)
  summary.md    → auto-generated conversation summary (written by summarization, enhancement 7)
```

`HierarchicalMemoryStore` loads and returns each file separately. `AgentOrchestrator` includes them in labeled sections: `## Persona`, `## Memory`, `## Conversation Summary`.

**Files:** `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`, `src/Honeybadger.Agent/AgentOrchestrator.cs`, `src/Honeybadger.Core/Models/AgentRequest.cs`

---

### Enhancement 7 — Token budget awareness for history *(Medium)*

**Problem:** A fixed count of 20 messages ignores sizes. A group with 20 long messages could overflow the model's context window. The code passes them all regardless.

**Fix:** Estimate token count (approximate: `chars / 4`) while building `ConversationHistory`. Stop adding messages when a configurable budget is reached (e.g., 8,000 tokens), even if fewer than N messages have been included. Add `Agent:ConversationHistoryTokenBudget` to `AgentOptions`.

**Files:** `src/Honeybadger.Host/Services/MessageLoopService.cs`, `src/Honeybadger.Core/Configuration/AgentOptions.cs`

---

### Enhancement 8 — Conversation summarization for long histories *(Medium-Large)*

**Problem:** When a group has more than 20 messages, old context is simply discarded. Important facts from early in a conversation are permanently lost once the rolling window moves past them.

**Fix:** Two-tier history:
- **Recent turns** (last N messages, verbatim) — same as now
- **Summary block** — an LLM-generated summary of older turns, stored in `groups/{name}/summary.md`

When history load returns more than N messages, trigger a background summarization task that asks the Copilot SDK to summarize turns outside the window and appends the result to `summary.md`. On subsequent invocations, `summary.md` is loaded as part of group context (Enhancement 6). The summary survives indefinitely and accumulates via append-or-replace.

**Files:** `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs`, `src/Honeybadger.Host/Services/MessageLoopService.cs`, new `SummarizationService`

---

### Enhancement 9 — Semantic memory search with sqlite-vec *(Large, future)*

**Problem:** With only a fixed recent-N window and flat file reads, the agent cannot retrieve relevant context from distant past conversations or large memory files when it needs it.

**Fix:** (OpenClaw-inspired) Embed conversation turns and memory file chunks into a SQLite vector index using the `sqlite-vec` extension. Add a `search_memory` IPC tool — agent calls it with a natural language query, host performs hybrid vector+BM25 search, returns ranked snippets. Embedding provider configurable (OpenAI embeddings, or a local model).

This enables queries like "what did I say about the API last month?" pulling from any past conversation, not just the recent window.

**Dependencies:** Embedding provider (OpenAI API or local), `sqlite-vec` SQLite extension, new DB migration for `chunks`/`chunks_vec`/`chunks_fts` tables.

**Files:** New `src/Honeybadger.Host/Memory/MemoryIndexManager.cs`, new IPC tool `search_memory`, new migration in `src/Honeybadger.Data.Sqlite/`

---

## 4. Implementation Roadmap

| Priority | Enhancement | Effort | Value |
|----------|-------------|--------|-------|
| 1 | Fix global CLAUDE.md content | Trivial | High — wrong content in context right now |
| 2 | Fix double-injection bug | Small | Medium — correctness fix |
| 3 | Cache memory files (FileSystemWatcher) | Small | Medium — I/O performance |
| 4 | Memory context for scheduled tasks | Small | High — tasks run completely blind |
| 5 | `update_memory` IPC tool | Medium | High — enables agents to learn |
| 6 | Separate persona/memory/summary files | Medium | Medium — clean foundation for future |
| 7 | Token budget awareness | Medium | Medium — safety against overflow |
| 8 | Conversation summarization | Medium-Large | High — long-running groups lose context |
| 9 | Semantic search (sqlite-vec) | Large | High — but requires embedding infra |

**Suggested milestones:**
- **Milestone A** (Enhancements 1–4): Quick wins — fix what's broken/missing right now
- **Milestone B** (Enhancements 5–6): Agent-writable memory — agents can learn and remember
- **Milestone C** (Enhancements 7–8): Smart history — no more lost context
- **Milestone D** (Enhancement 9): Semantic retrieval — full OpenClaw-style memory search
