# Honeybadger Plans

## Current State

Phases 1–6 are complete. Build is clean: 0 errors, 0 warnings, 44 tests passing.

What's been built: EF Core + SQLite, LocalAgentRunner (in-process), AgentOrchestrator (Copilot SDK), file-based IPC, named-pipe chat client, Serilog structured logging, streaming responses, conversation history, multi-agent delegation (router + specialists), agent-writable memory, token budget awareness, `IToolProvider` extensibility pattern.

## Active Plans

| File | Summary | Status |
|------|---------|--------|
| `07-advanced-memory.md` | Conversation summarization + semantic search (sqlite-vec) | 🔮 Future |
| `draft/08-bmad-sdlc-agents.md` | BMAD SDLC agent team — analyst, PM, architect, developer, QA, reviewer | 📝 Draft |
| `draft/09-multi-frontend-chat.md` | Multi-frontend chat — composite router, IFrontendProvider, Telegram adapter | 📝 Draft |
| `draft/10-linux-compatibility.md` | Linux compatibility — bug fixes, case-sensitive paths, stale comments, deployment docs | 📝 Draft |
| `draft/11-agent-pipeline-improvements.md` | Agent improvements — internal tags, memory guidance, task context_mode, transcript archival, typing heartbeat, error persistence | 📝 Draft |

## Tool Extensibility Pattern

Tools are registered via `IToolProvider` (`Honeybadger.Core.Interfaces`). `AgentToolFactory` iterates all registered providers — no switch statements, no knowledge of individual tool classes.

**To add a new tool set:**
1. Create `Honeybadger.Agent/Tools/{Feature}/` with tool classes and a `{Feature}ToolProvider : IToolProvider`
2. Add `ServiceCollectionExtensions.cs` with `AddXxxTools(this IServiceCollection, string ipcDir)`
3. Call it from `Program.cs` — the only file that changes

**Current registrations:**
- `AddCoreTools(ipcDir)` — IpcTools + AgentDelegationTools (`Tools/Core/`)

**Planned (Phase 8):**
- `AddSdlcTools(ipcDir)` — SprintStatusTools, GitTools, FileTools, ShellTools, EscalateTools (`Tools/Sdlc/`)

**Folder vs new project:** stay in a folder until a feature needs NuGet packages that don't belong in `Honeybadger.Agent` (e.g., a WhatsApp SDK, ML libraries).

## Codebase Patterns

| Pattern | Example location |
|---------|-----------------|
| `CorrelationId` | `Guid.NewGuid().ToString("N")[..12]` + `LogContext.PushProperty` |
| Scoped DB | `scopeFactory.CreateScope()` + `GetRequiredService<TRepo>()` |
| IPC request/response | Atomic write (temp + rename), poll for `{id}.response.json` — see `IpcTools.ListTasks` |
| Hosted service with non-DI params | `AddHostedService(sp => new Service(...))` — see `IpcWatcherService` registration |
| FileSystemWatcher | `FileBasedIpcTransport.cs` — reuse pattern for any file-watching need |
| Primary constructors | All services use C# 12 primary constructors |
| `IToolProvider` | `Tools/Core/CoreToolProvider.cs` — reference implementation |

## Verification

```bash
dotnet build Honeybadger.slnx    # 0 errors, 0 warnings
dotnet test Honeybadger.slnx     # 44 tests passing
```
