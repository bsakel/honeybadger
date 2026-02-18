# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Honeybadger is an in-process personal AI assistant built in C#/.NET 10, inspired by [nanoclaw](https://github.com/gavrielc/nanoclaw). Where nanoclaw uses TypeScript/Node.js, WhatsApp (Baileys), and the Claude Agent SDK, Honeybadger uses **C#/.NET 10**, a **console app** (Spectre.Console), **in-process agent execution** (LocalAgentRunner), and the **GitHub Copilot SDK**.

- **Original inspiration**: [gavrielc/nanoclaw](https://github.com/gavrielc/nanoclaw)
- **Remote**: https://github.com/bsakel/honeybadger.git
- **License**: MIT

## Feature Comparison with NanoClaw

| Feature | NanoClaw | Honeybadger | Notes |
|---|---|---|---|
| Language | TypeScript / Node.js | C# / .NET 10 | |
| AI SDK | Claude Agent SDK | GitHub Copilot SDK (`0.1.23`) | |
| Frontend | WhatsApp (Baileys) | Console (Spectre.Console) | Swappable via `IChatFrontend` |
| Database | Raw SQLite | EF Core with SQLite | Simple, file-based persistence |
| Agent Runtime | Apple Container / Docker | In-process (`LocalAgentRunner`) | No containerization overhead |
| IPC | File-based JSON | File-based JSON | Same pattern |
| Scheduling | Cron / Interval / Once | Cron / Interval / Once | Same types |
| Memory | Global + per-group `CLAUDE.md` | Global + per-group `CLAUDE.md` | Same pattern |
| Group queue | Per-group serialization | Per-group serialization + global concurrency limit | |
| Mount security | Allowlist in code | Allowlist JSON + blocked patterns + symlink resolution | |
| Logging | Console | Serilog (file rolling + LogContext enrichment) | |
| Tests | None visible | 36 tests across 4 projects | |
| Crash recovery (scheduler) | Cursor-based message recovery | Immediate tick on startup catches overdue tasks | |
| Trigger patterns | Regex per group | Configurable per group (not yet wired to routing) | |
| Per-group model | Via Claude SDK | Via `Groups:{name}:Model` config | |

### NanoClaw features not ported (by design)

- **WhatsApp integration** — replaced by console; `IChatFrontend` allows adding WhatsApp later if needed
- **`refresh_groups` / `register_group` IPC commands** — not needed for console mode
- **`context_mode` on scheduled tasks** (isolated vs full project mount) — not needed for in-process mode
- **Per-group container config overrides** — not applicable without containers
- **Environment variable filtering** — not applicable to in-process execution
- **Cursor-based message replay on crash** — not implemented (immediate scheduler tick handles task recovery)

### Honeybadger features not in NanoClaw

- **Structured logging** — Serilog with file rolling, `LogContext.PushProperty` for GroupName/MessageId/TaskId correlation
- **Rich console UI** — Spectre.Console panels, spinners, colored output, streaming token output
- **Graceful shutdown** — `IHostApplicationLifetime` cancellation, `Console.In.ReadLineAsync` with token support
- **Central package management** — `Directory.Build.props`, `Directory.Packages.props`
- **Comprehensive test suite** — 44 tests (unit, integration, scheduling, IPC, security)
- **Multi-agent collaboration** — Router agents delegate to specialist agents with custom tools and soul files
- **Agent-writable memory** — Agents can persist learned facts via `update_memory` IPC tool
- **Token budget awareness** — Conversation history respects configurable token budget, prioritizes recent messages
- **In-process agent execution** — `LocalAgentRunner` runs agents without containerization overhead
- **Streaming responses** — real-time token-by-token output from Copilot SDK
- **Conversation history in context** — automatic inclusion of recent messages in agent prompts
- **Bidirectional IPC** — response file pattern for agent queries (e.g., `list_tasks`)
- **CI/CD** — GitHub Actions workflow for automated build, test, and lint

## Implementation Status

Foundation complete, multi-agent collaboration working, enhanced memory system implemented:

| Phase | Status | What was built |
|---|---|---|
| Foundation (1-3) | ✅ Done | Solution scaffold, EF Core + SQLite (7 tables), LocalAgentRunner (in-process), AgentOrchestrator (Copilot SDK), IPC tools, mount security, named-pipe chat client, Serilog structured logging, streaming responses, conversation history |
| Multi-Agent Foundation (4) | ✅ Done | AgentConfiguration model, AgentRegistry (loads config/agents/*.json), soul files (agent personalities), example router + specialist configs, IPC message types for delegation |
| Multi-Agent Delegation (5) | ✅ Done | AgentDelegationTools (delegate_to_agent, list_available_agents), AgentToolFactory (config → AIFunction mapping), IpcWatcherService delegation handlers, MessageLoopService agent routing, specialist orchestration with custom tools |
| Memory Enhancements (6) | ✅ Done | update_memory IPC tool (agent-writable persistence), separate memory files (CLAUDE.md/MEMORY.md/summary.md), HierarchicalMemoryStore with FileSystemWatcher cache invalidation, token budget awareness (8000 tokens default), ConversationFormatter prioritizes recent messages |
| Advanced Memory (7) | 🔮 Future | Conversation summarization → summary.md, semantic search with sqlite-vec, search_memory IPC tool |

**Build**: `dotnet build Honeybadger.slnx` — 0 errors, 0 warnings
**Tests**: `dotnet test Honeybadger.slnx` — 44 passing (7 Core + 16 Integration + 21 Host)

### Current Architecture

Honeybadger runs entirely in-process with multi-agent collaboration:
- **Single process** — Host and agents run in the same .NET process
- **Multi-agent system** — Router agent delegates to specialist agents with custom tools and personalities (souls)
- **Enhanced memory** — Separate files for persona (CLAUDE.md), learned facts (MEMORY.md), summaries (summary.md)
- **Agent-writable memory** — Agents can persist information via `update_memory` tool
- **Token budget aware** — Conversation history respects 8000-token budget (configurable), prioritizes recent messages
- **SQLite only** — Simple, file-based persistence with EF Core
- **Named-pipe UI** — Headless service + separate chat client (but `IChatFrontend` makes it swappable)

### Multi-Agent Features

1. **Router Agent** — Primary orchestrator that analyzes requests and delegates to specialists
2. **Specialist Agents** — Domain-specific agents with custom tools and knowledge
3. **Dynamic Tool Loading** — AgentToolFactory maps agent configuration to AIFunction instances
4. **Soul Files** — Markdown files defining agent personalities and guidelines
5. **IPC-based Delegation** — Request/response pattern for inter-agent communication
6. **Agent Summary Injection** — Router automatically sees available specialists in context

### Memory System

1. **Hierarchical Memory** — Global (AGENT.md) + per-group files with caching
2. **Three-tier Group Memory**:
   - `CLAUDE.md` — Persona (read-only, defines group character)
   - `MEMORY.md` — Learned facts (agent-writable via update_memory tool)
   - `summary.md` — Conversation summaries (future: Phase 7)
3. **FileSystemWatcher Cache** — Automatic invalidation when files change
4. **Token Budget** — Configurable limit (default 8000), prioritizes recent conversation
5. **Attribution** — Memory updates track agent ID and timestamp

### Future Enhancement Opportunities

1. **Advanced Memory (Phase 7)** — Conversation summarization, semantic search with sqlite-vec
2. **WhatsApp / Telegram frontend** — `IChatFrontend` abstraction allows adding chat platform adapters
3. **Docker containerization** — Could add back for true sandboxing if needed (removed for simplicity)
4. **SQL Server support** — Could add back for scale-out scenarios (removed for simplicity)

## Architecture

Single-process design: all components run in the same .NET process.

```
HOST PROCESS (.NET)
  Console --> IChatFrontend
       |          Channel<ChatMessage>
       v
  MessageLoopService
       |
       +-- GroupQueue (per-group serialization + global concurrency limit)
       v
  LocalAgentRunner (in-process)
       |
       v
  AgentOrchestrator
       |          CopilotSession (per-group model)
       |          AIFunction tools (IPC-based)
       v
  IpcWatcherService <-- file-based IPC (data/ipc/)
       |
  SchedulerService (cron/interval/once)
       |
  CopilotCliService (SDK-managed, port 3100)
       |
  EF Core (SQLite)
```

**In-process architecture**: `LocalAgentRunner` creates `AgentOrchestrator` instances directly in the host process. Agents use IPC tools to communicate back to the host (schedule tasks, send messages, etc.). `CopilotCliService` manages the Copilot CLI lifecycle via the SDK, and all agents share the same CLI instance for model access.

## Solution Structure

```
honeybadger/
├── Honeybadger.slnx                       # Solution (XML format)
├── Directory.Build.props                  # Shared TFM (net10.0), nullable, implicit usings
├── Directory.Packages.props               # Central package version management
├── CLAUDE.md                              # This file
├── src/
│   ├── Honeybadger.Core/                  # Shared models, interfaces, config POCOs
│   │   ├── Configuration/                 # HoneybadgerOptions, AgentOptions, CopilotCliOptions, etc.
│   │   ├── Interfaces/                    # IChatFrontend, IAgentRunner, IIpcTransport
│   │   └── Models/                        # ChatMessage, AgentRequest/Response, IpcMessage, payloads
│   ├── Honeybadger.Data/                  # EF Core persistence
│   │   ├── HoneybadgerDbContext.cs        # 7 DbSets
│   │   ├── Entities/                      # ChatEntity, MessageEntity, ScheduledTaskEntity, etc.
│   │   └── Repositories/                  # MessageRepository, TaskRepository, SessionRepository, GroupRepository
│   ├── Honeybadger.Data.Sqlite/           # SQLite provider + InitialCreate migration
│   ├── Honeybadger.Host/                  # Host orchestrator
│   │   ├── Services/                      # CopilotCliService, MessageLoopService, SchedulerService, IpcWatcherService
│   │   ├── Agents/                        # LocalAgentRunner, MountSecurityValidator
│   │   ├── Ipc/                           # FileBasedIpcTransport (FileSystemWatcher + 500ms polling)
│   │   ├── Memory/                        # HierarchicalMemoryStore
│   │   └── Scheduling/                    # GroupQueue, CronExpressionEvaluator
│   ├── Honeybadger.Agent/                 # Agent orchestration (runs in-process)
│   │   ├── Program.cs                     # Entry point for standalone testing
│   │   ├── AgentOrchestrator.cs           # CopilotClient -> host CLI, session lifecycle, streaming
│   │   └── Tools/IpcTools.cs              # AIFunction tools: send_message, schedule_task, pause/resume/cancel/list_tasks
│   └── Honeybadger.Console/              # Console chat frontend
│       ├── Program.cs                     # Host builder, DI, hosted services
│       ├── ConsoleChat.cs                 # IChatFrontend via Spectre.Console
│       └── appsettings.json               # Full configuration
├── config/
│   └── mount-allowlist.json               # Filesystem security rules
├── groups/
│   └── main/CLAUDE.md                     # Main group memory file
├── data/                                  # SQLite DB + IPC directory (created at runtime)
└── tests/
    ├── Honeybadger.Core.Tests/            # 7 tests (models, config)
    ├── Honeybadger.Host.Tests/            # 13 tests (MessageRepository, GroupQueue, MessageLoop, Scheduler, CronEval)
    ├── Honeybadger.Agent.Tests/           # 0 tests (placeholder project)
    └── Honeybadger.Integration.Tests/     # 16 tests (IPC transport, mount security)
```

## Build and Run Commands

```bash
# Build entire solution
dotnet build Honeybadger.slnx

# Run the console app
dotnet run --project src/Honeybadger.Console

# Run all tests
dotnet test Honeybadger.slnx

# Run a single test project
dotnet test tests/Honeybadger.Core.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# EF Core migrations (from repo root)
dotnet ef migrations add MigrationName --project src/Honeybadger.Data.Sqlite --startup-project src/Honeybadger.Console
dotnet ef database update --project src/Honeybadger.Data.Sqlite --startup-project src/Honeybadger.Console
```

## Database Schema (7 tables)

| Table | Purpose | Key columns |
|---|---|---|
| `chats` | Group/conversation registry | Id, GroupName (unique) |
| `messages` | All messages | ExternalId, ChatId, Sender, Content, Timestamp, IsFromAgent |
| `scheduled_tasks` | Cron/interval/once tasks | GroupName, Name, ScheduleType, CronExpression, IntervalTicks, NextRunAt, Status |
| `task_run_logs` | Execution history | TaskId, StartedAt, CompletedAt, DurationMs, Status, Result, Error |
| `sessions` | Copilot SDK session IDs | ChatId, SessionId |
| `group_registrations` | Registered groups | GroupName, Folder, TriggerPattern, IsMain |
| `router_state` | Key-value host state | Key, Value |

## Configuration (appsettings.json)

```jsonc
{
  "Agent": {
    "DefaultModel": "claude-sonnet-4.5",
    "TimeoutSeconds": 300,
    "MaxConcurrentAgents": 3,
    "ConversationHistoryCount": 20,
    "CopilotCli": {
      "Port": 3100,
      "AutoStart": true,
      "ExecutablePath": "copilot",
      "Arguments": "--server --port {port}"
    }
  },
  "Groups": {
    "main": {
      "Model": null,
      "Trigger": null,
      "IsMain": true
    }
  },
  "Database": {
    "ConnectionString": "Data Source=data/honeybadger.db"
  },
  "Security": {
    "MountAllowlistPath": "config/mount-allowlist.json",
    "ResolveSymlinks": true
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Honeybadger": "Debug"
      }
    }
  }
}
```

## Key NuGet Packages

| Package | Version | Project | Purpose |
|---|---|---|---|
| `GitHub.Copilot.SDK` | 0.1.23 | Host, Agent | CopilotClient, sessions, events |
| `Microsoft.EntityFrameworkCore` | 9.0.1 | Data | Core EF |
| `Microsoft.EntityFrameworkCore.Sqlite` | 9.0.1 | Data.Sqlite | SQLite provider |
| `Microsoft.Extensions.Hosting` | 9.0.1 | Host, Console | IHostedService, DI, configuration |
| `Cronos` | 0.9.0 | Host | Cron expression parsing with timezone |
| `Spectre.Console` | 0.49.1 | Console | Rich terminal UI |
| `Serilog` | 4.2.0 | Host | Structured logging |
| `Serilog.Sinks.File` | 6.0.0 | Host | Rolling file log sink |

## Key Design Patterns

- **`IChatFrontend`**: Abstraction for UI frontends (`Channel<ChatMessage>` for incoming, `SendToUserAsync` for outgoing). Console implements it; WhatsApp or other frontends can be added.
- **SQLite via EF Core**: `HoneybadgerDbContext` has no `OnConfiguring`. Provider registered externally via `AddHoneybadgerSqlite()`.
- **File-based IPC**: Agent writes JSON to `data/ipc/`. Host watches via `FileSystemWatcher` + 500ms polling fallback. Atomic writes (temp + rename).
- **GroupQueue**: Per-group `Channel<Func<CancellationToken, Task>>` with `Lazy<Channel<...>>` for thread-safe single consumer. Global `SemaphoreSlim` for max concurrency.
- **Hierarchical memory**: Global `CLAUDE.md` (repo root) + per-group `CLAUDE.md` (in `groups/{name}/`).
- **In-process agent execution**: `LocalAgentRunner` creates `AgentOrchestrator` instances directly in the host process. No IPC overhead, direct method calls.
- **Copilot SDK session management**: `CopilotClient` managed by `CopilotCliService`; sessions created with `SessionConfig { Model, SystemMessage, Tools }`; responses collected via `TaskCompletionSource<string>` bridging `session.On(AssistantMessageEvent)`.

## Known Considerations

- **Copilot SDK is in technical preview** — API may change. Agent logic is abstracted behind interfaces to minimize refactoring impact.
- **FileSystemWatcher reliability** — May not fire immediately on some filesystems. Polling fallback (500ms) ensures IPC messages are always processed.
- **`Console.In.ReadLineAsync` cancellation** — Works on .NET 7+ but may degrade to blocking on some console implementations; `IHostApplicationLifetime` callback ensures channel completion regardless.
- **Single-process architecture** — All agents share the same process. No isolation between groups. Future: could add back Docker for sandboxing if needed.

## Logging Improvement Ideas (Future Enhancements)

The current logging implementation (CorrelationId-based tracing, dual log files, configurable levels) is solid for development and basic production use. Here are ideas for future enhancement:

### 1. **Structured JSON Logging**
- Switch output format from text to JSON for machine parsing
- Enables ingestion by log aggregation tools (Seq, Elasticsearch, Splunk, Azure Monitor)
- Use `Serilog.Formatting.Compact.CompactJsonFormatter`
- Example: `{ "timestamp": "2026-02-09T14:30:01.123Z", "level": "Debug", "correlationId": "a1b2c3d4e5f6", "sourceContext": "MessageLoopService", "message": "Processing message", "properties": { "groupName": "main", "contentLength": 12 } }`

### 2. **OpenTelemetry Integration**
- Replace custom CorrelationId with W3C Trace Context standard
- Add distributed tracing with `System.Diagnostics.Activity`
- Emit traces, metrics, and logs to OTLP endpoint
- Visualize request flows in Jaeger, Zipkin, or Application Insights
- Track spans: `message.received → queue.enqueue → agent.invoke → copilot.request → copilot.response → message.complete`

### 3. **Performance Metrics & Histograms**
- Track latency distributions (p50, p95, p99) for key operations:
  - Message processing end-to-end
  - Agent invocation time
  - Copilot SDK round-trip time
  - Database query time
  - IPC file write/read time
- Use `System.Diagnostics.Metrics` or Prometheus .NET client
- Export to Prometheus, Grafana, or Azure Monitor
- Alert on SLO violations (e.g., p95 > 5 seconds)

### 4. **Cost & Token Usage Tracking**
- Log token counts per request (prompt + completion)
- Aggregate cost per group, per model, per day
- Store in `token_usage` table: `{ CorrelationId, GroupName, Model, PromptTokens, CompletionTokens, Cost, Timestamp }`
- Dashboard showing daily/monthly spend trends
- Budget alerts via Serilog enrichers or custom metric exporters

### 5. **Log Sampling for High-Volume**
- At scale (1000s of messages/hour), Debug logs become expensive
- Implement adaptive sampling:
  - Always log errors/warnings
  - Sample Debug/Trace at 1% during normal operation
  - Increase to 100% when error rate spikes
- Use `Serilog.Filters.Expressions` or custom `ILogEventFilter`
- Example: `"Filter": [{ "Name": "ByExcluding", "Args": { "expression": "SamplingLevel = 'Debug' and Random() > 0.01" } }]`

### 6. **Component-Level Log Control**
- Granular overrides per class/namespace in `appsettings.json`:
  ```json
  "Serilog": {
    "MinimumLevel": {
      "Override": {
        "Honeybadger.Host.Services.MessageLoopService": "Debug",
        "Honeybadger.Host.Ipc": "Trace",
        "Honeybadger.Agent.AgentOrchestrator": "Information",
        "Honeybadger.Host.Scheduling": "Warning"
      }
    }
  }
  ```
- Hot-reload log levels without restart (Serilog.Settings.Configuration supports this)

### 7. **Error Rate & Alerting**
- Enrich logs with `ErrorCategory` (e.g., `CopilotTimeout`, `DatabaseError`, `SecurityRejection`)
- Track error rate per category per minute
- Emit alerts when error rate exceeds threshold
- Integration with PagerDuty, Slack, or email via Serilog sinks

### 8. **Correlation Across Scheduled Tasks**
- When a scheduled task is created via IPC, link its `CorrelationId` to the parent message's `CorrelationId`
- Add `ParentCorrelationId` field to `ScheduledTaskEntity`
- Log chain: `user_message[abc123] → schedule_task_ipc[abc123] → task_created[def456,parent=abc123] → task_run[def456]`
- Enables tracing: "Which user request created this failing scheduled task?"

### 9. **Audit Logging**
- Separate audit trail for security-sensitive operations:
  - Group registration/deletion
  - Task scheduling/cancellation
  - Mount security allowlist changes
  - Configuration updates
- Write to immutable append-only log or separate audit DB table
- Include: `{ Timestamp, Actor, Action, Resource, Outcome, Details }`

### 10. **Log Retention & Rotation Policies**
- Current: rolling by day, no cleanup
- Future: retention policy per log level
  - Debug logs: 7 days
  - Info logs: 30 days
  - Warning/Error logs: 90 days
- Use Serilog `retainedFileCountLimit` or external log management
- Archive old logs to blob storage (S3, Azure Blob) for compliance

### 11. **Request/Response Payload Logging (Sensitive Data)**
- Optionally log full `AgentRequest`/`AgentResponse` payloads for debugging
- Redact sensitive fields (API keys, user PII) before logging
- Enable via `"LogPayloads": true` config flag
- Use separate `honeybadger-payloads.log` file with stricter access controls

### 12. **Real-Time Log Streaming**
- Stream logs to SignalR hub for live dashboard
- WebSocket endpoint: `/logs/stream?correlationId=abc123`
- Real-time view of message flowing through pipeline in browser
- Useful for debugging production issues without SSH access

### 13. **Log Contextualization**
- Automatically enrich all logs with:
  - `MachineName` (useful in multi-instance deployments)
  - `ProcessId` (distinguish restarts)
  - `ThreadId` (diagnose thread pool exhaustion)
  - `AssemblyVersion` (correlate bugs with releases)
- Use `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Process`, `Serilog.Enrichers.Thread`

### 14. **Async Logging (Performance)**
- Current: synchronous writes to disk may block under high load
- Use `Serilog.Sinks.Async` wrapper around File sink
- Buffers log events in memory, writes in background thread
- Prevents I/O blocking during message processing
- Tune `bufferSize` and `blockWhenFull` for reliability vs. throughput

### Implementation Priority
1. **High value, low effort**: Structured JSON (#1), Component-level control (#6), Async logging (#14)
2. **High value, medium effort**: OpenTelemetry (#2), Performance metrics (#3), Log retention (#10)
3. **Medium value, medium effort**: Cost tracking (#4), Error alerting (#7), Audit logging (#9)
4. **Lower priority**: Sampling (#5), Correlation across tasks (#8), Payload logging (#11), Real-time streaming (#12), Additional enrichers (#13)
