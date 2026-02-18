# Phase 2 — Split Console into Service + Chat Client

## Goal

Split `Honeybadger.Console` (currently one process doing everything) into:

- **`Honeybadger.Console`** — headless backend service; all hosted services run here, exposes a named-pipe endpoint for chat clients
- **`Honeybadger.Chat`** (new project) — thin Spectre.Console UI; connects to the service via named pipe, bound to one group

Each chat terminal is bound to exactly one group (`--group <name>`, default `main`). Multiple terminals can run simultaneously, each on a different group.

**Prerequisite:** Phase 1 complete

---

## Named-Pipe Protocol

**Pipe name:** `honeybadger-chat`
**Format:** Newline-delimited JSON (NDJSON) — one `PipeMessage` object per line.

### Connection Handshake

1. Client connects to pipe
2. Client sends `{ "type": "register", "groupName": "main" }`
3. Service maps `groupName → StreamWriter` (last-writer-wins for same group)
4. Client enters input/render loops

### Message Types

| Direction | Type | Key Fields |
|-----------|------|------------|
| Client → Host | `register` | GroupName |
| Client → Host | `user_message` | GroupName, Content, Sender |
| Host → Client | `thinking_show` | GroupName |
| Host → Client | `thinking_hide` | GroupName |
| Host → Client | `stream_chunk` | GroupName, Chunk |
| Host → Client | `stream_done` | GroupName |
| Host → Client | `agent_message` | GroupName, Content, Sender, IsFromAgent |

---

## Step 1 — PipeMessage Model

**New file: `src/Honeybadger.Core/Models/PipeMessage.cs`**

```csharp
public record PipeMessage
{
    public required string Type { get; init; }
    public string? GroupName { get; init; }
    public string? Content { get; init; }
    public string? Sender { get; init; }
    public bool IsFromAgent { get; init; }
    public string? Chunk { get; init; }

    public static class Types
    {
        public const string Register = "register";
        public const string UserMessage = "user_message";
        public const string ThinkingShow = "thinking_show";
        public const string ThinkingHide = "thinking_hide";
        public const string StreamChunk = "stream_chunk";
        public const string StreamDone = "stream_done";
        public const string AgentMessage = "agent_message";
    }
}
```

---

## Step 2 — NamedPipeChatFrontend

**New file: `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs`**

Implements `IChatFrontend`. The `IChatFrontend` interface (unchanged) has 6 members:
- `ChannelReader<ChatMessage> IncomingMessages`
- `SendToUserAsync`, `ShowAgentThinkingAsync`, `HideAgentThinkingAsync`, `SendStreamChunkAsync`, `SendStreamCompleteAsync`

**Internal state:**
- `Channel<ChatMessage> _incoming` (unbounded)
- `Dictionary<string, StreamWriter> _groupClients` (group → pipe writer)
- `SemaphoreSlim _clientsLock(1, 1)`
- `CancellationTokenSource _cts`
- `Task _listenTask`

**Key methods:**
- `ListenLoopAsync`: Creates `NamedPipeServerStream`, waits for connection, fires-and-forgets `HandleClientAsync`
- `HandleClientAsync`: Reads first line as `register`, maps group to writer, then loops reading `user_message` → writes to `_incoming`
- `SendToGroupAsync`: Serializes `PipeMessage` to JSON, writes line to the group's pipe writer
- Each `IChatFrontend` method maps to appropriate `PipeMessage` type and calls `SendToGroupAsync`

---

## Step 3 — Honeybadger.Chat Project

**New file: `src/Honeybadger.Chat/Honeybadger.Chat.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Spectre.Console" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Honeybadger.Core\Honeybadger.Core.csproj" />
  </ItemGroup>
</Project>
```

**New file: `src/Honeybadger.Chat/Program.cs`**

Minimal — no DI, no hosted services:
1. Parse `--group <name>` (default "main")
2. Hook `Console.CancelKeyPress → cts.Cancel()`
3. Connect to `NamedPipeClientStream(".", "honeybadger-chat", InOut, Asynchronous)` with 5s timeout
4. Send register message
5. Run render task (pipe → Spectre.Console) and input task (stdin → pipe) concurrently
6. `await Task.WhenAny(renderTask, inputTask)`, cancel, cleanup

---

## Step 4 — Modify Honeybadger.Console

**Edit: `src/Honeybadger.Console/Program.cs`**
- Line 98: Change `AddSingleton<IChatFrontend, ConsoleChat>()` to `AddSingleton<IChatFrontend, NamedPipeChatFrontend>()`
- Add `using Honeybadger.Host.Ipc;`
- Remove startup banner (no longer a console UI process)

**Edit: `src/Honeybadger.Console/Honeybadger.Console.csproj`**
- Remove `<PackageReference Include="Spectre.Console" />`

**Delete: `src/Honeybadger.Console/ConsoleChat.cs`**

---

## Step 5 — Update Solution File

**Edit: `Honeybadger.slnx`**
- Add: `<Project Path="src/Honeybadger.Chat/Honeybadger.Chat.csproj" />`

---

## Files Summary

| Action | File |
|--------|------|
| New | `src/Honeybadger.Core/Models/PipeMessage.cs` |
| New | `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs` |
| New | `src/Honeybadger.Chat/Honeybadger.Chat.csproj` |
| New | `src/Honeybadger.Chat/Program.cs` |
| Edit | `src/Honeybadger.Console/Program.cs` |
| Edit | `src/Honeybadger.Console/Honeybadger.Console.csproj` |
| Delete | `src/Honeybadger.Console/ConsoleChat.cs` |
| Edit | `Honeybadger.slnx` |

---

## Test Plan

### Automated Tests

Write tests for:
1. **PipeMessage serialization** — round-trip serialize/deserialize for each message type
2. **NamedPipeChatFrontend routing** — verify messages route to correct group client

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass (no existing tests depend on ConsoleChat)
```

### Manual Verification

```bash
# Terminal 1 — headless service
dotnet run --project src/Honeybadger.Console

# Terminal 2 — chat on "main"
dotnet run --project src/Honeybadger.Chat -- --group main

# Terminal 3 — chat on "work"
dotnet run --project src/Honeybadger.Chat -- --group work
```

Expected:
- Each terminal sees only its own group's responses
- Kill and relaunch a chat terminal — service keeps running, reconnect works
- Start chat client before service → prints error and exits within 5 seconds
- Streaming responses appear token-by-token in the chat terminal
