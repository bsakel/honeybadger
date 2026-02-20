# Phase 8 — BMAD SDLC Agent Team (Draft)

## Goal

Port the [BMad Method v6 / BMAD_Openclaw](https://github.com/ErwanLorteau/BMAD_Openclaw/tree/dev) software-development-lifecycle orchestration pattern into Honeybadger. The result is a team of specialist AI agents (Analyst, PM, Architect, Developer, Scrum Master, QA, Reviewer) that hand off structured artifacts between each other through a shared sprint-status YAML file, guided by a router agent (BMad Master). New IPC tools (`read_sprint_status`, `update_sprint_status`, `run_git_diff`, `escalate`) extend the host to support this workflow.

**Prerequisite:** Phase 6 complete (multi-agent delegation, agent-writable memory, token budget all in place).

---

## Overview

### Pipeline

```
Idea
 → Product Brief       (analyst agent)
 → PRD                 (pm agent)
 → Architecture        (architect agent)
 → Epics & Stories     (scrum-master agent)
 → Readiness Check     (readiness-check agent)
 → [Sprint Loop]
     → Create Story    (scrum-master agent)
     → Dev Story       (developer agent)
     → Code Review     (reviewer agent)
     → QA Tests        (qa agent)
     → Sprint Status   (sprint-status query tool)
 → Retrospective       (scrum-master agent)
```

### Coordination Backbone

All agents read/write `data/sprint-status.yaml` via dedicated IPC tools. This YAML file is the shared state machine — no DB rows, no extra tables.

### Document-as-Checkpoint

Each output artifact (PRD, story, architecture) carries YAML frontmatter tracking workflow progress. An interrupted workflow resumes by reading `stepsCompleted` from the output document. No session state is stored elsewhere.

---

## Step 1 — Soul Files

**New files in `souls/`**

One markdown soul file per agent. These define character, guiding principles, and the step-by-step workflow the agent follows. They map directly to the `Soul` field in `AgentConfiguration` and are loaded by `AgentRegistry.LoadSoulFile()`.

| File | Agent | Character |
|---|---|---|
| `souls/bmad-master.md` | BMad Master (router) | Routes requests; operates in Normal or YOLO mode; injects available agents summary |
| `souls/analyst.md` | Mary — Business Analyst | "Treasure hunter" excited by patterns; frameworks: Porter's Five Forces, SWOT |
| `souls/pm.md` | John — Product Manager | Asks "WHY?" relentlessly; 8+ yrs B2B/consumer |
| `souls/architect.md` | Winston — System Architect | "Boring technology for stability"; append-only document construction |
| `souls/developer.md` | Amelia — Senior Developer | Ultra-succinct; Red-Green-Refactor; never lies about tests; HALT on 3 consecutive failures |
| `souls/scrum-master.md` | Bob — Scrum Master | Checklist-driven; owns sprint-status.yaml; creates and validates story files |
| `souls/qa.md` | Quinn — QA Engineer | "Ship it and iterate"; 5-step: detect framework → identify features → API tests → E2E tests → summarize |
| `souls/reviewer.md` | Adversarial Reviewer | Minimum 3 findings required; git-diff verified; severity: CRITICAL/HIGH/MEDIUM/LOW |

Each soul file follows the existing format in `souls/main.md`: plain markdown, no special delimiters.

Key soul-file conventions to port verbatim from BMAD_Openclaw:
- Developer: "HALT if 3 consecutive test failures — write a `HALT.md` and stop."
- Reviewer: "Fewer than 3 findings means you are not looking hard enough."
- Scrum Master: "NEVER produce time estimates. No hours, days, weeks, or timelines."
- BMad Master: YOLO mode — "If the user has enabled YOLO mode, auto-confirm all checkpoints."

---

## Step 2 — Agent Configuration Files

**New files in `config/agents/`**

One JSON config per agent. These are loaded by `AgentRegistry` at startup. All agents use the existing `AgentConfiguration` model (no model changes needed).

```jsonc
// config/agents/bmad-master.json
{
  "agentId": "bmad-master",
  "displayName": "BMad Master",
  "description": "SDLC orchestrator — routes requests to the appropriate specialist agent",
  "soul": "souls/bmad-master.md",
  "isRouter": true,
  "model": null,
  "tools": [
    "send_message",
    "delegate_to_agent",
    "list_available_agents",
    "read_sprint_status",
    "update_sprint_status"
  ]
}
```

```jsonc
// config/agents/developer.json
{
  "agentId": "developer",
  "displayName": "Amelia — Developer",
  "description": "Implements stories via Red-Green-Refactor; reads story file as sole input",
  "soul": "souls/developer.md",
  "isRouter": false,
  "model": null,
  "tools": [
    "send_message",
    "read_file",
    "write_file",
    "run_command",
    "run_git_diff",
    "read_sprint_status",
    "update_sprint_status"
  ]
}
```

```jsonc
// config/agents/reviewer.json
{
  "agentId": "reviewer",
  "displayName": "Adversarial Reviewer",
  "description": "Adversarial code review — minimum 3 findings, git-diff verified",
  "soul": "souls/reviewer.md",
  "isRouter": false,
  "model": null,
  "tools": [
    "send_message",
    "read_file",
    "write_file",
    "run_git_diff",
    "read_sprint_status",
    "update_sprint_status"
  ]
}
```

Remaining configs (analyst, pm, architect, scrum-master, qa) follow the same pattern with appropriate tool subsets. Specialist agents generally need: `send_message`, `read_file`, `write_file`, `read_sprint_status`, `update_sprint_status`.

---

## Step 3 — Sprint Status IPC Tools

**New file: `src/Honeybadger.Agent/Tools/SprintStatusTools.cs`**

Two IPC tools following the existing request/response file pattern (same as `list_tasks` in `IpcTools.cs`).

### `read_sprint_status(mode?)`

- `mode`: `"interactive"` (human summary) | `"data"` (machine-readable YAML) | `"validate"` (schema check). Defaults to `"data"`.
- Writes `IpcMessageType.ReadSprintStatus` IPC file with `ReadSprintStatusPayload { Mode }`.
- Polls for `{requestId}.response.json` (5s timeout, 200ms poll).
- Returns the sprint status content as a string.

### `update_sprint_status(epicKey?, storyKey?, newStatus, notes?)`

- `epicKey`: e.g. `"epic-1"`. Optional — omit to update only a story.
- `storyKey`: e.g. `"1-2-account-management"`. Optional — omit to update only an epic.
- `newStatus`: one of `backlog | in-progress | ready-for-dev | review | done | optional`.
- `notes`: optional freeform comment appended to the YAML entry.
- Writes `IpcMessageType.UpdateSprintStatus` IPC file with `UpdateSprintStatusPayload`.
- Polls for `{requestId}.response.json` (5s timeout, 200ms poll) for ACK.

Constructor: `string ipcDirectory, string groupName, ILogger, string correlationId` — same pattern as `IpcTools`.

---

## Step 4 — Git Diff IPC Tool

**New file: `src/Honeybadger.Agent/Tools/GitTools.cs`**

### `run_git_diff(baseSha?, paths?)`

- `baseSha`: compare HEAD against this SHA. Defaults to the merge-base of HEAD and main.
- `paths`: optional array of file paths to restrict the diff.
- Writes `IpcMessageType.RunGitDiff` IPC file with `RunGitDiffPayload { BaseSha, Paths }`.
- Host runs `git diff {baseSha} -- {paths}` in the project root (via `Process`).
- Polls for `{requestId}.response.json` (10s timeout, 200ms poll).
- Returns the raw diff string (truncated to 50 000 chars if necessary).

Used by: `reviewer` (cross-reference against story `File List`), `developer` (verify own changes).

---

## Step 5 — Escalate IPC Tool

**New file: `src/Honeybadger.Agent/Tools/EscalateTools.cs`**

### `escalate(title, finding, options[])`

- `title`: short title, e.g. `"Architecture assumption invalidated"`.
- `finding`: detailed description of the discovery.
- `options`: 2–4 string options for the user to choose from.
- Writes `IpcMessageType.Escalate` IPC file with `EscalatePayload`.
- Does **not** poll for a response — the agent tool returns immediately with `"Escalation submitted. Awaiting user decision."`.

**Host side (`IpcWatcherService`):**
1. Receives the `Escalate` IPC message.
2. Sends a formatted panel to the user via `IChatFrontend.SendToUserAsync`.
3. Writes an entry to `data/escalations.log` (append-only).
4. Pauses the group queue for the originating group (calls `GroupQueue.PauseAsync(groupName)`).
5. Waits for the user to respond via the chat frontend with one of the provided options.
6. Resumes the group queue once a response is received, passing the user's choice back as the next message in the group queue.

**`GroupQueue` changes needed:**
- Add `PauseAsync(string groupName)` and `ResumeAsync(string groupName)` — suspend consumption without draining the channel. Use a `SemaphoreSlim(0, 1)` per group that the consumer awaits before each dequeue.

---

## Step 6 — IpcWatcherService Handlers

**Edit: `src/Honeybadger.Host/Services/IpcWatcherService.cs`**

### 6A. New IPC message type cases

```csharp
case IpcMessageType.ReadSprintStatus:
    await HandleReadSprintStatusAsync(message, ct);
    break;
case IpcMessageType.UpdateSprintStatus:
    await HandleUpdateSprintStatusAsync(message, ct);
    break;
case IpcMessageType.RunGitDiff:
    await HandleRunGitDiffAsync(message, ct);
    break;
case IpcMessageType.Escalate:
    await HandleEscalateAsync(message, ct);
    break;
```

### 6B. `HandleReadSprintStatusAsync`

1. Deserialize `ReadSprintStatusPayload`.
2. Read `data/sprint-status.yaml` from disk (return empty YAML skeleton if not found).
3. If `mode == "interactive"`, format as human-readable markdown table.
4. If `mode == "validate"`, validate against schema (required keys, valid status values).
5. Write response file: `{requestId}.response.json` with `{ Content: string }`.

### 6C. `HandleUpdateSprintStatusAsync`

1. Deserialize `UpdateSprintStatusPayload`.
2. Read `data/sprint-status.yaml`.
3. Parse YAML, update the specified `epicKey` and/or `storyKey` to `newStatus`. Append `notes` as a comment if provided.
4. Write YAML back atomically (temp + rename).
5. Write ACK response file: `{requestId}.response.json` with `{ Success: true }`.

### 6D. `HandleRunGitDiffAsync`

1. Deserialize `RunGitDiffPayload { BaseSha, Paths }`.
2. Resolve `BaseSha`: if null, run `git merge-base HEAD main` to get the default.
3. Run `git diff {baseSha} -- {paths}` via `Process.Start` with `RedirectStandardOutput = true`.
4. Capture stdout, truncate to 50 000 chars if needed.
5. Write response file: `{requestId}.response.json` with `{ Diff: string }`.

### 6E. `HandleEscalateAsync`

See Step 5 above — send to user, write to escalation log, pause group queue.

---

## Step 7 — New IPC Model Types

**Edit: `src/Honeybadger.Core/Models/IpcMessage.cs`**

Add to the `IpcMessageType` enum:

```csharp
ReadSprintStatus,
UpdateSprintStatus,
RunGitDiff,
Escalate,
```

**New payload classes** (in `src/Honeybadger.Core/Models/` — follow existing payload file pattern):

```csharp
public record ReadSprintStatusPayload(string RequestId, string Mode = "data");

public record UpdateSprintStatusPayload(
    string RequestId,
    string? EpicKey,
    string? StoryKey,
    string NewStatus,
    string? Notes);

public record RunGitDiffPayload(string RequestId, string? BaseSha, string[]? Paths);

public record EscalatePayload(string RequestId, string Title, string Finding, string[] Options);
```

---

## Step 8 — SdlcToolProvider + ServiceCollectionExtensions

`AgentToolFactory` already iterates all registered `IToolProvider` implementations via DI — **no changes to `AgentToolFactory` or any existing file are needed**. The SDLC tools are wired in by adding a new `IToolProvider` implementation and registering it in `Program.cs`.

**New file: `src/Honeybadger.Agent/Tools/Sdlc/SdlcToolProvider.cs`**

```csharp
public class SdlcToolProvider(string ipcDirectory, ILoggerFactory loggerFactory) : IToolProvider
{
    public IEnumerable<AIFunction> GetTools(AgentConfiguration agentConfig, string groupName, string correlationId)
    {
        // Each tool class exposes GetAll() — yield all tools from each
        foreach (var tool in new SprintStatusTools(ipcDirectory, groupName, ...).GetAll())
            yield return tool;
        foreach (var tool in new GitTools(ipcDirectory, groupName, ...).GetAll())
            yield return tool;
        foreach (var tool in new FileTools(repoRoot, mountValidator, ...).GetAll())
            yield return tool;
        foreach (var tool in new ShellTools(repoRoot, ...).GetAll())
            yield return tool;
        foreach (var tool in new EscalateTools(ipcDirectory, groupName, ...).GetAll())
            yield return tool;
    }
}
```

**New file: `src/Honeybadger.Agent/Tools/Sdlc/ServiceCollectionExtensions.cs`**

```csharp
public static IServiceCollection AddSdlcTools(this IServiceCollection services, string ipcDirectory)
{
    services.AddSingleton<IToolProvider>(sp =>
        new SdlcToolProvider(ipcDirectory, sp.GetRequiredService<ILoggerFactory>(), ...));
    return services;
}
```

**Edit: `src/Honeybadger.Console/Program.cs`** — one new line to opt in:

```csharp
builder.Services.AddCoreTools(ipcDir);
builder.Services.AddSdlcTools(ipcDir);  // ← add this line
builder.Services.AddSingleton<AgentToolFactory>();
```

`FileTools` and `ShellTools` are two new lightweight tool classes (not providers):

- **`FileTools`** — `read_file(path)` and `write_file(path, content)`. Validates paths against `MountSecurityValidator` before any read or write. Returns file content or write confirmation.
- **`ShellTools`** — `run_command(command, workingDirectory?)`. Restricted to an allowlist of safe executables (`dotnet`, `git`, `npm`, `yarn`, `pnpm`). Returns stdout + stderr + exit code.

---

## Step 9 — Document-as-Checkpoint (`FileStateStore`)

**New file: `src/Honeybadger.Host/State/FileStateStore.cs`**

Reads and writes YAML frontmatter on markdown files to track workflow progress. This enables interrupted workflows to resume exactly where they left off without any database state.

```csharp
public class FileStateStore
{
    // Read stepsCompleted from frontmatter of a markdown document
    public Task<int[]> GetStepsCompletedAsync(string filePath);

    // Write updated stepsCompleted back to the document's frontmatter
    public Task MarkStepCompletedAsync(string filePath, int stepNumber);

    // Read any arbitrary frontmatter key
    public Task<string?> GetFrontmatterValueAsync(string filePath, string key);
}
```

YAML frontmatter block is the `---`-delimited header at the top of the markdown file:

```markdown
---
stepsCompleted: [1, 2, 3]
workflowType: prd
inputDocuments: ["docs/product-brief.md"]
---
# PRD: My Project
...
```

`FileStateStore` parses this block with a minimal YAML parser (no new NuGet dependency — the frontmatter schema is simple enough for regex + `string.Split`). Writes are atomic (temp + rename).

**YOLO mode interaction**: In YOLO mode, `stepsCompleted` is not needed for checkpoint skipping, but it is still written so that if YOLO is disabled mid-workflow the document remains resumable.

---

## Step 10 — YOLO Mode

**Edit: `src/Honeybadger.Core/Configuration/HoneybadgerOptions.cs`**

Add:
```csharp
public bool YoloMode { get; set; } = false;
```

**Edit: `src/Honeybadger.Console/appsettings.json`**

```jsonc
"YoloMode": false
```

**YOLO mode behaviour (implemented in soul files):**

The YOLO flag is injected into every agent's system prompt via `AgentOrchestrator.BuildSystemContext`:

```
## Execution Mode
YOLO mode is ENABLED. Auto-confirm all checkpoints. Do not wait for user input at [y/n] prompts — proceed immediately with the recommended path.
```

When `YoloMode = false`, the line reads:
```
## Execution Mode
Standard mode. Wait for user confirmation at all checkpoints before proceeding.
```

Agents themselves implement the checkpoint/confirm loop in their soul files. The host only provides the flag.

**Runtime toggle**: The router agent can call `update_sprint_status` with `notes: "yolo=true"` as a convention, or the user can set it in `appsettings.json`. A future enhancement could add an `enable_yolo` IPC tool.

---

## Step 11 — Templates Directory

**New directory: `templates/`**

Markdown templates for artifact output documents. Agents are instructed in their soul files to use these templates when creating new documents. Templates are not loaded by the host at runtime — they are referenced in soul files and read via the `read_file` tool.

| File | Purpose |
|---|---|
| `templates/product-brief.md` | Idea → brief: vision, target users, constraints, success metrics |
| `templates/prd.md` | PRD: goals, user stories, functional/non-functional requirements, FR traceability |
| `templates/architecture.md` | System design: components, data model, API contracts, ADRs |
| `templates/story.md` | Story: AC format, tasks, Dev Notes, Dev Agent Record, File List |
| `templates/sprint-status.yaml` | Sprint tracker: epic/story status state machine |
| `templates/review.md` | Code review: severity table (CRITICAL/HIGH/MEDIUM/LOW), AC traceability |
| `templates/readiness-report.md` | GO/NO-GO/CONDITIONAL GO assessment with FR traceability matrix |

### `templates/story.md` structure (critical — drives dev and review agents)

```markdown
---
stepsCompleted: []
workflowType: story
epicKey: epic-1
storyKey: 1-2-account-management
status: ready-for-dev
---
# Story {storyKey}: {Title}

## Acceptance Criteria
- AC1: ...
- AC2: ...

## Tasks
- [ ] Task 1
- [ ] Task 2

## Dev Notes
<!-- Exhaustive context: architecture refs, library versions, previous story learnings, gotchas -->

## Dev Agent Record
<!-- Populated by developer agent during implementation -->
### Completion Notes
### Deviations from AC

## File List
<!-- Populated by developer agent after implementation -->
- src/Foo/Bar.cs
- tests/Foo/BarTests.cs
```

### `templates/sprint-status.yaml` structure

```yaml
development_status:
  epic-1: backlog          # backlog | in-progress | done
  1-1-user-authentication: done          # backlog | ready-for-dev | in-progress | review | done
  1-2-account-management: in-progress
  1-3-plant-data-model: ready-for-dev
  epic-1-retrospective: optional         # optional | done
```

---

## Step 12 — Groups Directory Setup

**New directories under `groups/`**

The SDLC workflow expects a conventional directory layout for project artifacts:

```
groups/
  main/
    CLAUDE.md              # existing persona file
    MEMORY.md              # existing agent-writable memory
    docs/
      planning/            # product-brief.md, prd.md, architecture.md
      stories/             # {epic}-{story}.md files
      reviews/             # review-{storyKey}-{date}.md files
      qa/                  # test-summary-{storyKey}.md files
      escalations/         # escalation log
```

No code changes needed — these directories are created by agents at runtime via `write_file`. The `docs/` path prefix is passed to agents as a structured input when the router delegates (`PLANNING_ARTIFACTS`, `IMPLEMENTATION_ARTIFACTS` — mirroring BMAD_Openclaw's `sessions_spawn` inputs).

---

## New IPC Tool Summary

| Tool | Class | Used by |
|---|---|---|
| `read_sprint_status` | `SprintStatusTools` | All agents |
| `update_sprint_status` | `SprintStatusTools` | scrum-master, developer, reviewer, qa |
| `run_git_diff` | `GitTools` | developer, reviewer |
| `escalate` | `EscalateTools` | All agents (emergency halt) |
| `read_file` | `FileTools` | All specialist agents |
| `write_file` | `FileTools` | All specialist agents |
| `run_command` | `ShellTools` | developer, qa |

---

## Files Summary

| Action | File |
|---|---|
| New | `souls/bmad-master.md` |
| New | `souls/analyst.md` |
| New | `souls/pm.md` |
| New | `souls/architect.md` |
| New | `souls/developer.md` |
| New | `souls/scrum-master.md` |
| New | `souls/qa.md` |
| New | `souls/reviewer.md` |
| New | `config/agents/bmad-master.json` |
| New | `config/agents/analyst.json` |
| New | `config/agents/pm.json` |
| New | `config/agents/architect.json` |
| New | `config/agents/developer.json` |
| New | `config/agents/scrum-master.json` |
| New | `config/agents/qa.json` |
| New | `config/agents/reviewer.json` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/SprintStatusTools.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/GitTools.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/EscalateTools.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/FileTools.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/ShellTools.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/SdlcToolProvider.cs` |
| New | `src/Honeybadger.Agent/Tools/Sdlc/ServiceCollectionExtensions.cs` |
| New | `src/Honeybadger.Host/State/FileStateStore.cs` |
| New | `templates/product-brief.md` |
| New | `templates/prd.md` |
| New | `templates/architecture.md` |
| New | `templates/story.md` |
| New | `templates/sprint-status.yaml` |
| New | `templates/review.md` |
| New | `templates/readiness-report.md` |
| Edit | `src/Honeybadger.Core/Models/IpcMessage.cs` — 4 new enum values |
| Edit | `src/Honeybadger.Core/Models/` — 4 new payload record files |
| Edit | `src/Honeybadger.Core/Configuration/HoneybadgerOptions.cs` — `YoloMode` flag |
| Edit | `src/Honeybadger.Host/Services/IpcWatcherService.cs` — 4 new handlers |
| Edit | `src/Honeybadger.Console/Program.cs` — add `AddSdlcTools(ipcDir)` |
| Edit | `src/Honeybadger.Agent/AgentOrchestrator.cs` — inject YOLO mode flag into system prompt |
| Edit | `src/Honeybadger.Console/appsettings.json` — `YoloMode: false` |
| New | `tests/Honeybadger.Host.Tests/SprintStatusHandlerTests.cs` |
| New | `tests/Honeybadger.Host.Tests/GitDiffHandlerTests.cs` |
| New | `tests/Honeybadger.Integration.Tests/FileStateStoreTests.cs` |
| New | `tests/Honeybadger.Integration.Tests/FileToolsSecurityTests.cs` |

---

## Implementation Order

Steps are ordered so each is independently testable before the next begins.

| Step | Effort | Risk | Dependency |
|---|---|---|---|
| 1 — Soul files | Low | Low | None |
| 2 — Agent configs | Low | Low | Step 1 |
| 3 — SprintStatusTools + IPC types | Medium | Low | None |
| 4 — GitTools | Medium | Low | None |
| 5 — EscalateTools + GroupQueue pause | Medium | Medium | Existing GroupQueue |
| 6 — IpcWatcherService handlers | Medium | Low | Steps 3, 4, 5 |
| 7 — IPC model types | Low | Low | Step 6 |
| 8 — AgentToolFactory extensions | Low | Low | Steps 3, 4, 5 |
| 9 — FileStateStore | Medium | Low | None |
| 10 — YOLO mode flag | Low | Low | Step 1 |
| 11 — Templates | Low | Low | Step 1 |
| 12 — Groups directory convention | Low | Low | Step 11 |

**Recommended batch order:**
1. Steps 1, 2, 10, 11 together — pure content, zero code risk
2. Steps 3, 4, 7 together — new tools + IPC types, no existing code touched
3. Step 9 — `FileStateStore`, standalone utility
4. Step 5 — `EscalateTools` + `GroupQueue` changes (highest risk, isolated)
5. Steps 6, 8 — wire everything into host services
6. Step 12 — documentation / directory setup

---

## Test Plan

### Automated Tests

**`tests/Honeybadger.Host.Tests/SprintStatusHandlerTests.cs`**
1. `HandleReadSprintStatus_DataMode_ReturnsYaml` — mock sprint-status.yaml on disk, verify parsed response
2. `HandleReadSprintStatus_MissingFile_ReturnsSkeleton` — file not found → empty YAML skeleton returned
3. `HandleUpdateSprintStatus_Story_UpdatesStatus` — write then read, verify status changed
4. `HandleUpdateSprintStatus_InvalidStatus_ReturnsError` — reject values outside the allowed enum

**`tests/Honeybadger.Host.Tests/GitDiffHandlerTests.cs`**
1. `HandleRunGitDiff_ValidBaseSha_ReturnsDiff` — stub `git diff` process, verify diff returned
2. `HandleRunGitDiff_NullBaseSha_ComputesMergeBase` — verify `git merge-base` is called first
3. `HandleRunGitDiff_LargeDiff_TruncatesAt50000` — verify truncation

**`tests/Honeybadger.Integration.Tests/FileStateStoreTests.cs`**
1. `GetStepsCompleted_NoFrontmatter_ReturnsEmpty`
2. `MarkStepCompleted_First_WritesFrontmatter`
3. `MarkStepCompleted_Subsequent_AppendToExisting`
4. `MarkStepCompleted_Atomic_FileIsNeverCorrupted` — write from two tasks concurrently, verify valid YAML

**`tests/Honeybadger.Integration.Tests/FileToolsSecurityTests.cs`**
1. `ReadFile_AllowedPath_ReturnsContent`
2. `ReadFile_BlockedPath_Throws`
3. `WriteFile_OutsideMount_Throws`
4. `RunCommand_AllowedExecutable_Succeeds`
5. `RunCommand_BlockedExecutable_Throws`

```bash
dotnet build Honeybadger.slnx    # 0 errors, 0 warnings
dotnet test Honeybadger.slnx     # All tests pass (target: 44 + ~15 new = ~59)
```

### Manual Verification

```bash
# Start service
dotnet run --project src/Honeybadger.Console

# Connect chat client
dotnet run --project src/Honeybadger.Chat -- --group main

# Trigger the pipeline
> I want to build a habit-tracking app
```

Expected log flow:
```
[INF] Selected agent bmad-master (router) for group main
[DBG] BMad Master delegating to analyst
[INF] IPC DelegateToAgent → analyst
[INF] Analyst writing product-brief.md
[INF] Analyst completed → delegating back to bmad-master
[DBG] BMad Master delegating to pm
[INF] PM writing prd.md
...
```

Expected user experience: BMad Master introduces the pipeline, invites the user to refine the product brief, then hands off through the chain, surfacing each artifact for review before proceeding.
