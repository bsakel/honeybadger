# Phase 7 — Advanced Memory (Future)

## Goal

Two advanced memory features that require significant infrastructure:
- **7A** Conversation summarization via LLM
- **7B** Semantic memory search with vector embeddings

**Prerequisite:** Phase 6 complete (separate memory files, token budget in place)

**Status:** Future — these features are deferred until the core multi-agent system is stable and production-tested.

---

## 7A. Conversation Summarization

### Problem

When a group exceeds the message history window, old context is simply discarded. Important facts from early in a conversation are permanently lost.

### Design

Two-tier history:
- **Recent turns** (last N messages, verbatim) — same as current
- **Summary block** — LLM-generated summary of older turns, stored in `groups/{name}/summary.md`

### Implementation Sketch

1. New `SummarizationService` (or method in `MessageLoopService`)
2. After processing a message, check if group's total message count exceeds a threshold (e.g., 2x history window)
3. If so, extract the oldest batch of messages outside the current window
4. Send a summarization prompt to the Copilot SDK:
   ```
   Summarize the following conversation. Focus on key facts, decisions, user preferences,
   and any commitments made. Be concise (under 500 words).

   [conversation turns...]
   ```
5. Write result to `groups/{name}/summary.md` (overwrite or append)
6. Phase 6B already set up `HierarchicalMemoryStore.LoadGroupSummary()` and `## Conversation Summary` section in `BuildSystemContext`

### Considerations

- Summarization uses model tokens (cost)
- Should run asynchronously to not block message processing
- May want a configurable threshold and summary size limit
- Could accumulate summaries over time (append) or regenerate (overwrite)

### Files

- New: `src/Honeybadger.Host/Memory/SummarizationService.cs`
- Edit: `src/Honeybadger.Host/Services/MessageLoopService.cs` — trigger summarization
- Edit: `src/Honeybadger.Core/Configuration/AgentOptions.cs` — add summarization config

---

## 7B. Semantic Memory Search with sqlite-vec

### Problem

With only a fixed recent-N window and flat file reads, agents cannot retrieve relevant context from distant past conversations or large memory files.

### Design (OpenClaw-inspired)

1. **Embedding pipeline**: Embed conversation turns and memory file chunks into vectors
2. **Storage**: SQLite vector index using `sqlite-vec` extension
   - `chunks` table: id, content, source, timestamp, embedding
   - `chunks_vec`: vector similarity search (ANN)
   - `chunks_fts`: BM25 full-text search
3. **Retrieval**: `search_memory` IPC tool — agent calls with natural language query, host performs hybrid search
4. **Ranking**: Cosine similarity + BM25 scores, optionally with temporal decay

### Implementation Sketch

1. New `MemoryIndexManager` service
   - Watches `MEMORY.md` and `summary.md` for changes
   - Chunks content into ~500-token segments
   - Embeds via configurable provider (OpenAI, local model)
   - Stores in SQLite vector tables

2. New `search_memory` IPC tool
   - Agent calls: `search_memory("what did the user say about Python?")`
   - Host embeds the query, searches `chunks_vec` (vector) + `chunks_fts` (BM25)
   - Returns top-K ranked snippets with source attribution

3. New DB migration for vector tables

### Dependencies

- `sqlite-vec` NuGet package (or equivalent)
- Embedding provider (OpenAI Embeddings API, or local llama.cpp/sentence-transformers)
- New `EmbeddingOptions` config section

### Considerations

- This is the largest single feature — may warrant its own sub-phases
- Embedding provider choice affects cost, latency, and offline capability
- Need to decide: embed conversation turns incrementally or batch?
- Need to handle re-indexing when memory files are manually edited

### Files

- New: `src/Honeybadger.Host/Memory/MemoryIndexManager.cs`
- New: `src/Honeybadger.Agent/Tools/MemorySearchTool.cs` (or add to IpcTools)
- New: DB migration in `src/Honeybadger.Data.Sqlite/Migrations/`
- Edit: `src/Honeybadger.Core/Models/IpcMessage.cs` — add `SearchMemory` type
- Edit: `src/Honeybadger.Core/Models/IpcPayloads.cs` — add search payloads
- Edit: `src/Honeybadger.Host/Services/IpcWatcherService.cs` — add search handler
- New: `src/Honeybadger.Core/Configuration/EmbeddingOptions.cs`

---

## Test Plan

### 7A Tests

1. Summarization produces valid markdown output
2. Summary is written to correct group directory
3. Summary appears in agent context on next invocation
4. Summarization only triggers when threshold is exceeded

### 7B Tests

1. Memory chunks are correctly stored in SQLite
2. Vector search returns relevant results
3. BM25 search returns keyword matches
4. Hybrid ranking combines scores correctly
5. `search_memory` IPC tool returns formatted results
6. Re-indexing works when memory files change

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass
```

### Manual Verification

**Summarization:**
```bash
# Have a long conversation (50+ messages)
# Check groups/main/summary.md is created
# Start a new session — agent should reference facts from the summary
```

**Semantic search:**
```bash
# Discuss multiple topics over several conversations
# Ask: "What did I say about Python last week?"
# Agent should use search_memory tool and retrieve relevant past turns
```
