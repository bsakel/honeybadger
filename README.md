# Honeybadger

A personal AI assistant framework built in C#/.NET 10. Honeybadger runs a team of specialized AI agents in-process, orchestrated through a structured multi-agent pipeline, with file-based IPC, SQLite persistence, and a rich console UI via Spectre.Console.

## Features

- **Multi-agent collaboration** — Router agents delegate to specialists; each agent has its own personality (soul file), tools, and optional model override
- **Extensible tool system** — Tools registered via `IToolProvider`; new tool sets added as opt-in modules without touching existing code
- **In-process execution** — Agents run directly in the host process via `LocalAgentRunner`; no containerization overhead
- **Task scheduling** — Cron expressions, fixed intervals, and one-time tasks with timezone support
- **Three-tier memory** — Per-group `CLAUDE.md` (persona), `MEMORY.md` (agent-writable facts), `summary.md` (future summaries)
- **Token budget awareness** — Conversation history respects a configurable token limit (default 8000), prioritizing recent messages
- **Streaming responses** — Real-time token-by-token output from the Copilot SDK
- **Named-pipe UI** — Headless service + separate chat client; swappable via `IChatFrontend`
- **SQLite persistence** — EF Core with 7 tables; file-based, zero-configuration
- **Mount security** — Allowlist + blocked patterns + symlink resolution before any filesystem access
- **Structured logging** — Serilog with file rolling and `LogContext` correlation (GroupName, CorrelationId)
- **44 tests** — Unit, integration, scheduling, IPC, and security coverage

## How It Works

```
User message (named pipe)
        |
        v
  MessageLoopService — routes via GroupQueue (per-group serialization)
        |
        v
  LocalAgentRunner — creates AgentOrchestrator in-process
        |
        v
  AgentOrchestrator — CopilotSession with soul + tools + memory context
        |
        v
  Agent streams response; IPC tool calls written as JSON files to data/ipc/
        |
        v
  IpcWatcherService — routes tool calls (schedule, delegate, memory, etc.)
        |
        v
  Response displayed via Spectre.Console
```

Groups run serially (one message at a time per group). A global `SemaphoreSlim` caps total concurrent agents across all groups.

## Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the GitHub Copilot CLI.

```bash
git clone https://github.com/bsakel/honeybadger.git
cd honeybadger
dotnet build Honeybadger.slnx
dotnet test Honeybadger.slnx

# Terminal 1 — headless service
dotnet run --project src/Honeybadger.Console

# Terminal 2 — chat client
dotnet run --project src/Honeybadger.Chat -- --group main
```

## Configuration

`src/Honeybadger.Console/appsettings.json`:

```jsonc
{
  "Agent": {
    "DefaultModel": "claude-sonnet-4.5",
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
    "main": { "IsMain": true }
  },
  "Database": {
    "ConnectionString": "Data Source=data/honeybadger.db"
  }
}
```

## Agent Configuration

Agents are defined by a JSON config and a markdown soul file. Drop both files in their directories — no code changes needed.

**Router agent** (`config/agents/main.json`):
```json
{
  "agentId": "main",
  "displayName": "Main Agent",
  "description": "Primary orchestrator — analyzes requests and delegates to specialists",
  "soul": "souls/main.md",
  "isRouter": true,
  "tools": ["send_message", "delegate_to_agent", "list_available_agents", "update_memory"]
}
```

**Specialist agent** (`config/agents/scheduler.json`):
```json
{
  "agentId": "scheduler",
  "displayName": "Scheduler",
  "description": "Manages scheduled tasks and reminders",
  "soul": "souls/scheduler.md",
  "model": "claude-sonnet-4.5",
  "isRouter": false,
  "tools": ["schedule_task", "list_tasks", "pause_task", "resume_task", "cancel_task"]
}
```

Soul files are plain markdown — they define the agent's character, guiding principles, and workflow steps.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    HOST PROCESS (.NET)                    │
│                                                           │
│  IChatFrontend (NamedPipeChatFrontend)                   │
│       │                                                   │
│       ▼                                                   │
│  MessageLoopService                                       │
│    ├─ AgentRegistry  (config/agents/*.json)               │
│    ├─ AgentToolFactory  (iterates IToolProvider chain)    │
│    └─ GroupQueue  (per-group serialization + concurrency) │
│       │                                                   │
│       ▼                                                   │
│  LocalAgentRunner (in-process)                            │
│       │                                                   │
│       ▼                                                   │
│  AgentOrchestrator                                        │
│    ├─ CopilotClient → host CLI (port 3100)               │
│    ├─ CopilotSession (streaming)                          │
│    └─ Tools (from IToolProvider chain)                    │
│         ├─ CoreToolProvider                               │
│         │    ├─ IpcTools (send_message, schedule_task…)   │
│         │    └─ AgentDelegationTools (router only)        │
│         └─ [future providers registered via AddXxxTools]  │
│                                                           │
│  IpcWatcherService  (watches data/ipc/)                   │
│  SchedulerService   (cron / interval / once)              │
│  HierarchicalMemoryStore  (CLAUDE.md + MEMORY.md + cache) │
│  CopilotCliService  (SDK-managed CLI lifecycle)           │
│  EF Core (SQLite — 7 tables)                              │
└──────────────────────────────────────────────────────────┘

CHAT CLIENT (separate process)
  └─ Named pipe "honeybadger-chat" — NDJSON protocol
```

## Project Structure

```
honeybadger/
├── src/
│   ├── Honeybadger.Core/           # Interfaces (IToolProvider, IChatFrontend…), models, config POCOs
│   ├── Honeybadger.Data/           # EF Core DbContext (7 DbSets)
│   ├── Honeybadger.Data.Sqlite/    # SQLite provider + migrations
│   ├── Honeybadger.Host/           # Host orchestration
│   │   ├── Agents/                 # AgentRegistry, AgentToolFactory, LocalAgentRunner
│   │   ├── Memory/                 # HierarchicalMemoryStore
│   │   ├── Ipc/                    # FileBasedIpcTransport
│   │   ├── Scheduling/             # GroupQueue, CronExpressionEvaluator
│   │   └── Services/               # MessageLoopService, IpcWatcherService, SchedulerService
│   ├── Honeybadger.Agent/          # Agent orchestration (runs in-process)
│   │   ├── AgentOrchestrator.cs
│   │   └── Tools/
│   │       └── Core/               # CoreToolProvider, IpcTools, AgentDelegationTools
│   ├── Honeybadger.Console/        # Headless service entry point
│   └── Honeybadger.Chat/           # Chat client (named-pipe)
├── tests/                          # 44 tests across 4 projects
├── config/
│   ├── agents/                     # Agent configurations (*.json)
│   └── mount-allowlist.json        # Filesystem security
├── souls/                          # Agent personality files (*.md)
├── groups/
│   └── {groupName}/
│       ├── CLAUDE.md               # Persona (read-only)
│       └── MEMORY.md               # Learned facts (agent-writable)
├── plans/                          # Implementation roadmap
│   ├── draft/                      # Work-in-progress plans
│   └── archive/                    # Completed phase plans
└── .github/workflows/              # CI pipeline
```

## Development

### Adding a New Tool Set

Tools are discovered via `IToolProvider`. Adding a new group of tools requires no changes to existing code.

1. Create `src/Honeybadger.Agent/Tools/{Feature}/` with your tool classes
2. Add `{Feature}ToolProvider.cs` implementing `IToolProvider`:
```csharp
public class MyToolProvider(string ipcDirectory, ILoggerFactory loggerFactory) : IToolProvider
{
    public IEnumerable<AIFunction> GetTools(AgentConfiguration agentConfig, string groupName, string correlationId)
    {
        foreach (var tool in new MyTools(ipcDirectory, groupName, ...).GetAll())
            yield return tool;
    }
}
```
3. Add `ServiceCollectionExtensions.cs`:
```csharp
public static IServiceCollection AddMyTools(this IServiceCollection services, string ipcDir)
{
    services.AddSingleton<IToolProvider>(sp => new MyToolProvider(ipcDir, sp.GetRequiredService<ILoggerFactory>()));
    return services;
}
```
4. Call `builder.Services.AddMyTools(ipcDir)` in `Program.cs`

> **When to use a separate project instead of a folder:** only when the feature pulls in NuGet packages that don't belong in `Honeybadger.Agent` (e.g., a WhatsApp SDK, ML libraries).

### Adding a New IPC Handler

1. Add the new `IpcMessageType` enum value in `Honeybadger.Core/Models/IpcMessage.cs`
2. Add a payload record in `Honeybadger.Core/Models/`
3. Add the tool method to the appropriate tool class and register it in `GetAll()`
4. Add the handler `case` in `IpcWatcherService.cs`

### Database Migrations

```bash
dotnet ef migrations add MigrationName \
  --project src/Honeybadger.Data.Sqlite \
  --startup-project src/Honeybadger.Console

dotnet ef database update \
  --project src/Honeybadger.Data.Sqlite \
  --startup-project src/Honeybadger.Console
```

### Running Tests

```bash
dotnet test Honeybadger.slnx
dotnet test tests/Honeybadger.Host.Tests          # single project
dotnet test --filter "FullyQualifiedName~MyTest"  # single test
```

## Security

Filesystem access is validated via `MountSecurityValidator` before any read or write:
- Allowlist defined in `config/mount-allowlist.json`
- Hardcoded blocked patterns: `/etc`, `/sys`, `/proc`, `.ssh/`, `.aws/`, `.env`
- Symlinks resolved and validated against the allowlist

Shell execution (future `ShellTools`) is restricted to an explicit executable allowlist (`dotnet`, `git`, `npm`, `yarn`, `pnpm`).

## Roadmap

| Phase | Status | Summary |
|---|---|---|
| 1–6 | ✅ Complete | Foundation, multi-agent, memory, streaming, named-pipe, logging |
| 7 | 🔮 Future | Advanced memory — conversation summarization, semantic search (sqlite-vec) |
| 8 | 📝 Draft | BMAD SDLC agent team — analyst, PM, architect, developer, QA, reviewer |

**BMAD (Phase 8 draft)** introduces a full software-development-lifecycle agent pipeline: a team of specialist agents that hand off structured artifacts (PRDs, architecture docs, story files) through a shared `sprint-status.yaml` coordination file. See `plans/draft/08-bmad-sdlc-agents.md`.

## License

MIT — see [LICENSE](LICENSE) for details.
