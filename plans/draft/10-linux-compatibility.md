# Plan 10 — Linux Compatibility Fixes

## Context

Honeybadger is currently Windows-only in practice. The codebase is almost entirely cross-platform .NET, but has a few bugs (affecting both platforms), stale comments from a retired containerized architecture, and missing documentation for Linux-specific behavior. These fixes prepare the codebase for running on Linux.

## Changes

### 1. Fix `LocalAgentRunner.cs` — wrong root path (bug, both platforms)

**File:** `src/Honeybadger.Host/Agents/LocalAgentRunner.cs:32`

Change `AppContext.BaseDirectory` → `Directory.GetCurrentDirectory()` to match every other component. Currently the IPC directory resolves to `bin/Debug/net10.0/data/ipc/` instead of `data/ipc/` relative to repo root.

### 2. Fix `Agent/Program.cs` — stale `/workspace/ipc` fallback (bug + stale comment)

**File:** `src/Honeybadger.Agent/Program.cs:47-48`

- Replace fallback `"/workspace/ipc"` with `Path.Combine(Directory.GetCurrentDirectory(), "data", "ipc")`
- Update comment from "IPC directory: /workspace/ipc (bind-mounted from host)" to reflect in-process architecture

### 3. Fix `MountSecurityValidator.cs` — case sensitivity on Linux

**File:** `src/Honeybadger.Host/Agents/MountSecurityValidator.cs`

Use `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` to select `OrdinalIgnoreCase` (Windows) vs `Ordinal` (Linux/macOS). Affects path comparisons in allowlist checking and blocked pattern matching.

### 4. Fix stale comment in `FileBasedIpcTransport.cs`

**File:** `src/Honeybadger.Host/Ipc/FileBasedIpcTransport.cs:10`

Change "NTFS filesystem compat" → "filesystem compat" (or similar). The polling fallback is relevant on all platforms.

### 5. Fix stale comment in `IpcTools.cs`

**File:** `src/Honeybadger.Agent/Tools/Core/IpcTools.cs:10`

Remove "/workspace/ipc/ (bind-mounted to host)" reference. Update to reflect current in-process IPC architecture.

### 6. Add named pipe Linux note

**File:** `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs`

Add a comment near the pipe name constant explaining that on Linux, .NET maps named pipes to Unix domain sockets at `/tmp/CoreFxPipe_<name>`.

### 7. Add startup working-directory check

**File:** `src/Honeybadger.Console/Program.cs`

After host builder setup, verify that `config/` and `data/` directories exist relative to CWD. If not, log a clear error message explaining the app must be run from the repo root (or set `WorkingDirectory` in systemd). Fail fast rather than silently misbehaving.

### 8. Document Linux deployment

**File:** `README.md`

Add a short section covering:
- Run from repo root (or set `WorkingDirectory=` in systemd unit)
- Named pipe socket location on Linux (`/tmp/CoreFxPipe_honeybadger-chat`)
- Copilot CLI must be installed and on `PATH`

## Files Changed

| File | Change |
|------|--------|
| `src/Honeybadger.Host/Agents/LocalAgentRunner.cs` | `AppContext.BaseDirectory` → `Directory.GetCurrentDirectory()` |
| `src/Honeybadger.Agent/Program.cs` | Fix fallback path + stale comment |
| `src/Honeybadger.Host/Agents/MountSecurityValidator.cs` | Platform-aware string comparison |
| `src/Honeybadger.Host/Ipc/FileBasedIpcTransport.cs` | Fix stale "NTFS" comment |
| `src/Honeybadger.Agent/Tools/Core/IpcTools.cs` | Fix stale container comment |
| `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs` | Add Linux socket path comment |
| `src/Honeybadger.Console/Program.cs` | Add startup CWD validation |
| `README.md` | Add Linux deployment section |

## Verification

```bash
dotnet build Honeybadger.slnx    # 0 errors, 0 warnings
dotnet test Honeybadger.slnx     # all 44 tests pass
```

Manual: run from a non-repo directory and verify the startup check produces a clear error message.
