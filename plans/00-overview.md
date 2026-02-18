# Honeybadger Consolidated Roadmap

## Context

This roadmap consolidates four previous plan documents (memory bug fixes, service/chat split, console logging, multi-agent collaboration) into a single dependency-ordered sequence of 7 phases. Each phase is self-contained, testable, and scoped to roughly one implementation session.

## Execution Order

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7
```

Architectural split first (Phases 1-3), then multi-agent on top of the final architecture (Phases 4-5), then memory enhancements (Phases 6-7).

## Dependency Graph

```
Phase 1 (memory bugfixes)
  │
  └──→ Phase 2 (service/chat split)
         │
         └──→ Phase 3 (console logging)
                │
                └──→ Phase 4 (multi-agent foundation)
                       │
                       └──→ Phase 5 (multi-agent delegation)
                              │
                              └──→ Phase 6 (memory enhancements)
                                     │
                                     └──→ Phase 7 (advanced memory — future)
```

## Phase Index

| Phase | File | Summary | Status |
|-------|------|---------|--------|
| 1 | `01-memory-bugfixes.md` | Fix CLAUDE.md content, double-injection bug, memory caching, scheduler memory | ✅ Complete |
| 2 | `02-service-chat-split.md` | Split console into headless service + named-pipe chat client | ✅ Complete |
| 3 | `03-console-logging.md` | Add Serilog console sink to headless service | ✅ Complete |
| 4 | `04-multi-agent-foundation.md` | AgentConfiguration model, AgentRegistry, example configs/souls | ✅ Complete |
| 5 | `05-multi-agent-delegation.md` | Delegation tools, IPC handlers, message routing | ✅ Complete |
| 6 | `06-memory-enhancements.md` | Agent-writable memory, separate files, token budget | ✅ Complete |
| 7 | `07-advanced-memory.md` | Summarization + semantic search | 🔮 Future |

## Test Count Progress

| Phase | Status | Test Count |
|-------|--------|------------|
| Baseline | ✅ | 36 |
| Phase 1-6 | ✅ Complete | 44 (7 Core + 16 Integration + 21 Host) |
| Phase 7 | 🔮 Future | TBD |

**Current Build Status**: ✅ 0 errors, 0 warnings, 44 tests passing

## Verification (Every Phase)

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass (including new ones)
```

## Critical Files Touched Across Multiple Phases

| File | Phases | Why |
|------|--------|-----|
| `src/Honeybadger.Console/Program.cs` | 1, 2, 3, 4, 5 | DI composition root; all new services registered here |
| `src/Honeybadger.Host/Services/MessageLoopService.cs` | 1, 5 | Message routing; double-injection fix then multi-agent routing |
| `src/Honeybadger.Host/Memory/HierarchicalMemoryStore.cs` | 1, 6 | Memory loading; caching then separate files |
| `src/Honeybadger.Host/Services/IpcWatcherService.cs` | 5, 6 | IPC routing hub; delegation handlers then update_memory |
| `src/Honeybadger.Agent/AgentOrchestrator.cs` | 5, 6 | System prompt construction; soul/tools then labeled sections |
| `src/Honeybadger.Core/Models/AgentRequest.cs` | 4, 6 | Request model; multi-agent fields then memory fields |
| `src/Honeybadger.Core/Models/IpcMessage.cs` | 4 | IPC enum; all new message types added at once |
| `src/Honeybadger.Core/Configuration/AgentOptions.cs` | 1, 6 | Config; scheduler history then token budget |

## Codebase Patterns to Reuse

1. **CorrelationId**: `Guid.NewGuid().ToString("N")[..12]` + `LogContext.PushProperty`
2. **Scoped DB**: `scopeFactory.CreateScope()` + `GetRequiredService<TRepo>()`
3. **IPC request/response**: Atomic write (temp + rename), poll for `{id}.response.json` (see `ListTasks` in `IpcTools.cs`)
4. **Factory DI**: `AddHostedService(sp => new Service(...))` for services needing non-DI params (see `IpcWatcherService`)
5. **FileSystemWatcher**: Already used in `FileBasedIpcTransport.cs` — reuse same pattern for memory caching
6. **Primary constructor pattern**: All services use C# 12 primary constructors (see `MessageLoopService`, `IpcWatcherService`, etc.)
