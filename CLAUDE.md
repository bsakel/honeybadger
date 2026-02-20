# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Honeybadger is a personal AI assistant framework built in **C#/.NET 10**. It runs a team of specialist AI agents in-process, orchestrated through file-based IPC, with SQLite persistence and a Spectre.Console UI.

- **Remote**: https://github.com/bsakel/honeybadger.git
- **License**: MIT

**Stack:**
- Language: C# / .NET 10
- AI SDK: GitHub Copilot SDK (`0.1.23`)
- UI: Spectre.Console via `IChatFrontend` (swappable — console today, WhatsApp or web tomorrow)
- Database: EF Core + SQLite (7 tables, file-based, zero-config)
- Agent runtime: In-process (`LocalAgentRunner`) — no containerization overhead
- Logging: Serilog (file rolling + `LogContext` enrichment)

## Implementation Status

| Phase | Status | What was built |
|---|---|---|
| 1–3 | ✅ Done | Solution scaffold, EF Core + SQLite (7 tables), LocalAgentRunner, AgentOrchestrator (Copilot SDK), IPC tools, mount security, named-pipe chat client, Serilog structured logging, streaming responses, conversation history |
| 4 | ✅ Done | AgentConfiguration model, AgentRegistry (loads config/agents/*.json), soul files, example router + specialist configs, IPC message types for delegation |
| 5 | ✅ Done | AgentDelegationTools (delegate_to_agent, list_available_agents), IpcWatcherService delegation handlers, MessageLoopService agent routing, specialist orchestration |
| 6 | ✅ Done | update_memory IPC tool, separate memory files (CLAUDE.md/MEMORY.md/summary.md), HierarchicalMemoryStore with FileSystemWatcher cache, token budget awareness, ConversationFormatter |
| 6.5 | ✅ Done | IToolProvider interface (Honeybadger.Core), Tools/Core/ subfolder, CoreToolProvider, AddCoreTools() — extensible tool registration without modifying AgentToolFactory |
| 7 | 🔮 Future | Conversation summarization → summary.md, semantic search with sqlite-vec, search_memory IPC tool |
| 8 | 📝 Draft | BMAD SDLC agent team — analyst, PM, architect, developer, QA, reviewer; sprint-status.yaml coordination; Tools/Sdlc/ provider (see plans/draft/08-bmad-sdlc-agents.md) |

**Build**: `dotnet build Honeybadger.slnx` — 0 errors, 0 warnings
**Tests**: `dotnet test Honeybadger.slnx` — 44 passing (7 Core + 16 Integration + 21 Host)

## Architecture

```
HOST PROCESS (.NET)
  IChatFrontend (NamedPipeChatFrontend)
       |
       v
  MessageLoopService
       |
       +-- AgentRegistry  (loads config/agents/*.json)
       +-- AgentToolFactory  (iterates IEnumerable<IToolProvider>)
       +-- GroupQueue  (per-group Channel + global SemaphoreSlim)
       |
       v
  LocalAgentRunner (in-process)
       |
       v
  AgentOrchestrator
       |          CopilotSession (per-group model override)
       |          AIFunction tools (from IToolProvider chain)
       v
  IpcWatcherService <-- file-based IPC (data/ipc/)
       |
  SchedulerService (cron/interval/once)
       |
  HierarchicalMemoryStore (CLAUDE.md + MEMORY.md cache + FileSystemWatcher)
       |
  CopilotCliService (SDK-managed, port 3100)
       |
  EF Core (SQLite)
```

**In-process architecture**: `LocalAgentRunner` creates `AgentOrchestrator` instances directly in the host process. No IPC overhead, direct method calls.

**Tool extensibility**: `AgentToolFactory` iterates all registered `IToolProvider` implementations from DI and returns their tools. New tool sets are registered via `AddXxxTools(ipcDir)` in `Program.cs` — no changes to `AgentToolFactory` or any existing file.

## Solution Structure

```
honeybadger/
├── Honeybadger.slnx                          # Solution (XML format)
├── Directory.Build.props                     # Shared TFM (net10.0), nullable, implicit usings
├── Directory.Packages.props                  # Central package version management
├── CLAUDE.md                                 # This file
├── src/
│   ├── Honeybadger.Core/                     # Shared models, interfaces, config POCOs
│   │   ├── Configuration/                    # HoneybadgerOptions, AgentOptions, CopilotCliOptions, etc.
│   │   ├── Interfaces/                       # IChatFrontend, IAgentRunner, IIpcTransport, IToolProvider
│   │   └── Models/                           # ChatMessage, AgentRequest/Response, IpcMessage, payloads
│   ├── Honeybadger.Data/                     # EF Core persistence
│   │   ├── HoneybadgerDbContext.cs           # 7 DbSets
│   │   ├── Entities/                         # ChatEntity, MessageEntity, ScheduledTaskEntity, etc.
│   │   └── Repositories/                     # MessageRepository, TaskRepository, SessionRepository, GroupRepository
│   ├── Honeybadger.Data.Sqlite/              # SQLite provider + InitialCreate migration
│   ├── Honeybadger.Host/                     # Host orchestrator
│   │   ├── Services/                         # CopilotCliService, MessageLoopService, SchedulerService, IpcWatcherService
│   │   ├── Agents/                           # LocalAgentRunner, MountSecurityValidator, AgentRegistry, AgentToolFactory
│   │   ├── Ipc/                              # FileBasedIpcTransport (FileSystemWatcher + 500ms polling)
│   │   ├── Memory/                           # HierarchicalMemoryStore
│   │   └── Scheduling/                       # GroupQueue, CronExpressionEvaluator
│   ├── Honeybadger.Agent/                    # Agent orchestration (runs in-process)
│   │   ├── Program.cs                        # Entry point for standalone testing
│   │   ├── AgentOrchestrator.cs              # CopilotClient → host CLI, session lifecycle, streaming
│   │   └── Tools/
│   │       └── Core/                         # CoreToolProvider, IpcTools, AgentDelegationTools, AddCoreTools()
│   └── Honeybadger.Console/                  # Console chat frontend
│       ├── Program.cs                        # Host builder, DI, hosted services
│       ├── ConsoleChat.cs                    # IChatFrontend via Spectre.Console
│       └── appsettings.json                  # Full configuration
├── config/
│   ├── agents/                               # Agent configs (*.json) — loaded by AgentRegistry
│   └── mount-allowlist.json                  # Filesystem security rules
├── souls/                                    # Agent personality files (*.md) — soul field in agent config
├── groups/
│   └── {groupName}/
│       ├── CLAUDE.md                         # Persona (read-only)
│       └── MEMORY.md                         # Learned facts (agent-writable via update_memory)
├── templates/                                # Document templates for BMAD workflow (Phase 8, future)
├── data/                                     # SQLite DB + IPC directory (created at runtime)
└── tests/
    ├── Honeybadger.Core.Tests/               # 7 tests (models, config)
    ├── Honeybadger.Host.Tests/               # 21 tests (MessageRepository, GroupQueue, MessageLoop, Scheduler, CronEval)
    ├── Honeybadger.Agent.Tests/              # 0 tests (placeholder project)
    └── Honeybadger.Integration.Tests/        # 16 tests (IPC transport, mount security)
```

## Build and Run Commands

```bash
# Build entire solution
dotnet build Honeybadger.slnx

# Terminal 1 — headless service
dotnet run --project src/Honeybadger.Console

# Terminal 2 — chat client
dotnet run --project src/Honeybadger.Chat -- --group main

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
    "ConversationHistoryTokenBudget": 8000,
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

- **`IToolProvider`**: The tool extensibility point. `AgentToolFactory` iterates all registered `IToolProvider` implementations via `IEnumerable<IToolProvider>` from DI. New tool sets are added by implementing `IToolProvider` and calling `AddXxxTools()` from `Program.cs`. No changes to `AgentToolFactory` or any existing code.

  ```
  Program.cs calls:
    AddCoreTools(ipcDir)        → CoreToolProvider (IpcTools + AgentDelegationTools)
    AddSdlcTools(ipcDir)        → SdlcToolProvider (Phase 8 — future)
  ```

  **Folder vs project boundary:** Keep tool sets in `Honeybadger.Agent/Tools/{Feature}/` until a feature requires NuGet packages that don't belong in `Honeybadger.Agent` (e.g., a WhatsApp SDK, ML libraries). At that point, extract to a new `.csproj`.

- **`IChatFrontend`**: UI abstraction — `Channel<ChatMessage>` for incoming, `SendToUserAsync` for outgoing. Console (`NamedPipeChatFrontend`) is current; WhatsApp or Blazor can be added without changing any other code.

- **SQLite via EF Core**: `HoneybadgerDbContext` has no `OnConfiguring`. Provider registered externally via `AddHoneybadgerSqlite()`.

- **File-based IPC**: Agent writes JSON to `data/ipc/`. Host watches via `FileSystemWatcher` + 500ms polling fallback. Atomic writes (temp + rename).

- **GroupQueue**: Per-group `Channel<Func<CancellationToken, Task>>` with `Lazy<Channel<...>>` for thread-safe single consumer. Global `SemaphoreSlim` for max concurrency.

- **Hierarchical memory**: Global `AGENT.md` (repo root) + per-group `CLAUDE.md` / `MEMORY.md` with `FileSystemWatcher` cache invalidation.

- **Copilot SDK session management**: `CopilotClient` managed by `CopilotCliService`; sessions created with `SessionConfig { Model, SystemMessage, Tools }`; responses collected via `TaskCompletionSource<string>` bridging `session.On(AssistantMessageEvent)`.

- **IPC request/response**: Atomic write (temp + rename), poll for `{id}.response.json` with timeout (see `ListTasks` in `IpcTools.cs`). Used for: `list_tasks`, `delegate_to_agent`, `list_available_agents`.

## Agent Configuration

Agent configs live in `config/agents/*.json`, loaded by `AgentRegistry` at startup. Soul files (markdown) define personality. No code changes needed to add an agent — drop two files and restart.

```jsonc
{
  "agentId": "my-agent",
  "displayName": "My Agent",
  "description": "What this agent does — shown to the router via list_available_agents",
  "soul": "souls/my-agent.md",
  "isRouter": false,
  "model": null,           // null = use Agent:DefaultModel
  "tools": ["send_message", "schedule_task"]
}
```

Available tool names (from `CoreToolProvider`): `send_message`, `schedule_task`, `pause_task`, `resume_task`, `cancel_task`, `list_tasks`, `update_memory`, `delegate_to_agent` (router only), `list_available_agents` (router only).

## Memory System

Three-tier per-group memory under `groups/{groupName}/`:

| File | Owner | Purpose |
|---|---|---|
| `CLAUDE.md` | Human (read-only to agents) | Persona — defines the agent's character and role |
| `MEMORY.md` | Agent (via `update_memory` tool) | Learned facts, user preferences, session notes |
| `summary.md` | Future (Phase 7) | Conversation summaries for long-term context |

`HierarchicalMemoryStore` caches all files with `FileSystemWatcher` invalidation. Token budget (default 8000, configurable) ensures the combined memory + conversation history fits in context — recent messages are prioritized.

## Known Considerations

- **Copilot SDK is in technical preview** — API may change. Agent logic is abstracted behind interfaces to minimize refactoring impact.
- **FileSystemWatcher reliability** — May not fire immediately on some filesystems. Polling fallback (500ms) ensures IPC messages are always processed.
- **Single-process architecture** — All agents share the same process. No isolation between groups. Could add Docker back for sandboxing if needed.

## Logging Improvement Ideas

See the detailed logging enhancement list at the bottom of the original CLAUDE.md (archived in git history). Summary of priority items:

1. **High value, low effort**: Structured JSON output, component-level log level control, async logging
2. **High value, medium effort**: OpenTelemetry tracing, performance metrics, log retention policies
3. **Medium value**: Cost/token tracking, error rate alerting, audit logging
