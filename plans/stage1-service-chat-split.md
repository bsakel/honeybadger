# Stage 1 — Split Console into Service + Chat Client

## Goal

Split `Honeybadger.Console` (currently one process doing everything) into:

- **`Honeybadger.Console`** — headless backend service; all hosted services run here, exposes a named-pipe endpoint for chat clients
- **`Honeybadger.Chat`** (new project) — thin Spectre.Console UI; connects to the service via named pipe, bound to one group

Each chat terminal is bound to exactly one group (`--group <name>`, default `main`). The service routes responses only to the client registered for that group, matching the existing `GroupQueue` per-group design. Multiple terminals can run simultaneously, each on a different group.

---

## Named-Pipe Protocol

**Pipe name:** `honeybadger-chat`

**Format:** Newline-delimited JSON (NDJSON) — one `PipeMessage` JSON object per line.

### New file: `src/Honeybadger.Core/Models/PipeMessage.cs`

```csharp
namespace Honeybadger.Core.Models;

public record PipeMessage
{
    public required string Type { get; init; }
    public string? GroupName  { get; init; }
    public string? Content    { get; init; }  // user_message / agent_message
    public string? Sender     { get; init; }  // agent_message
    public bool    IsFromAgent { get; init; } // agent_message
    public string? Chunk      { get; init; }  // stream_chunk

    public static class Types
    {
        public const string Register     = "register";      // client → host (on connect)
        public const string UserMessage  = "user_message";  // client → host
        public const string ThinkingShow = "thinking_show"; // host → client
        public const string ThinkingHide = "thinking_hide"; // host → client
        public const string StreamChunk  = "stream_chunk";  // host → client
        public const string StreamDone   = "stream_done";   // host → client
        public const string AgentMessage = "agent_message"; // host → client
    }
}
```

### Connection handshake

1. Client connects to pipe
2. Client immediately sends `{ "type": "register", "groupName": "main" }`
3. Service maps `groupName → StreamWriter`; new registration replaces any previous for same group (last-writer-wins)
4. Client enters its normal input/render loops

| Direction     | Type          | Key fields                               |
|---------------|---------------|------------------------------------------|
| Client → Host | register      | GroupName                                |
| Client → Host | user_message  | GroupName, Content, Sender               |
| Host → Client | thinking_show | GroupName                                |
| Host → Client | thinking_hide | GroupName                                |
| Host → Client | stream_chunk  | GroupName, Chunk                         |
| Host → Client | stream_done   | GroupName                                |
| Host → Client | agent_message | GroupName, Content, Sender, IsFromAgent  |

---

## Step 1 — `PipeMessage` model

Already described above. This lives in `Honeybadger.Core` so both the service and the chat client can reference it without circular dependencies.

No changes to `Honeybadger.Core.csproj` needed — it has no package dependencies beyond what's already there.

---

## Step 2 — `NamedPipeChatFrontend` in `Honeybadger.Host`

### New file: `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs`

Implements `IChatFrontend`. Starts a background listen loop in the constructor (same pattern as the old `ConsoleChat._inputTask = Task.Run(...)`).

**Internal state:**
```csharp
private readonly Channel<ChatMessage> _incoming = Channel.CreateUnbounded<ChatMessage>();
private readonly Dictionary<string, StreamWriter> _groupClients = new();
private readonly SemaphoreSlim _clientsLock = new(1, 1);
private readonly CancellationTokenSource _cts = new();
private readonly Task _listenTask;
```

**Constructor:** inject `IHostApplicationLifetime` and `ILogger<NamedPipeChatFrontend>`.
- Register `lifetime.ApplicationStopping` to cancel `_cts` and complete `_incoming.Writer`
- `_listenTask = Task.Run(() => ListenLoopAsync(_cts.Token))`

**`ListenLoopAsync`:**
```
while not cancelled:
    create NamedPipeServerStream("honeybadger-chat", InOut,
        MaxAllowedServerInstances, Byte, Asynchronous)
    await pipe.WaitForConnectionAsync(ct)
    _ = HandleClientAsync(pipe, ct)   // fire-and-forget
```

**`HandleClientAsync(pipe, ct)`:**
1. Create `StreamReader reader` and `StreamWriter writer { AutoFlush = true }` on the pipe
2. Read first line → deserialize as `PipeMessage`; must be `register` type — extract `GroupName`
3. Acquire `_clientsLock`, set `_groupClients[groupName] = writer`, release lock
4. Loop reading subsequent lines:
   - `user_message` → `await _incoming.Writer.WriteAsync(new ChatMessage { GroupName, Content, Sender }, ct)`
5. On `null` line (disconnect) or any exception: acquire lock, `_groupClients.Remove(groupName)`, release, dispose pipe

**Output routing — all 5 IChatFrontend output methods:**
```
async Task SendToGroupAsync(string groupName, PipeMessage msg, CancellationToken ct):
    await _clientsLock.WaitAsync(ct)
    try:
        if _groupClients.TryGetValue(groupName, out var writer):
            var json = JsonSerializer.Serialize(msg)
            await writer.WriteLineAsync(json.AsMemory(), ct)
        else:
            logger.LogWarning("No chat client connected for group {Group}", groupName)
    catch (IOException):
        // client disconnected mid-write — remove it
        _groupClients.Remove(groupName)
    finally:
        _clientsLock.Release()
```

Map each `IChatFrontend` method to `SendToGroupAsync`:
- `SendToUserAsync(msg)` → `AgentMessage`, use `msg.GroupName`
- `ShowAgentThinkingAsync(groupName)` → `ThinkingShow`
- `HideAgentThinkingAsync(groupName)` → `ThinkingHide`
- `SendStreamChunkAsync(groupName, chunk)` → `StreamChunk`, set `Chunk = chunk`
- `SendStreamCompleteAsync(groupName)` → `StreamDone`

**`IncomingMessages`:** `=> _incoming.Reader`

**`Dispose`:** cancel `_cts`, dispose `_clientsLock`.

---

## Step 3 — New `Honeybadger.Chat` project

### New file: `src/Honeybadger.Chat/Honeybadger.Chat.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Honeybadger.Core\Honeybadger.Core.csproj" />
  </ItemGroup>

</Project>
```

### New file: `src/Honeybadger.Chat/Program.cs`

Minimal — no DI, no hosted services.

```
1. Parse args: --group <name> (default: "main")
2. Set up CancellationTokenSource; hook Console.CancelKeyPress → cts.Cancel()
3. AnsiConsole.MarkupLine connecting banner showing group name
4. Create NamedPipeClientStream(".", "honeybadger-chat", InOut, Asynchronous)
5. try: await pipe.ConnectAsync(timeout: 5s, ct)
   catch: print "Service not running on pipe 'honeybadger-chat'" and return 1
6. var reader = new StreamReader(pipe)
   var writer = new StreamWriter(pipe) { AutoFlush = true }
7. Send register: await writer.WriteLineAsync(JsonSerializer.Serialize(
       new PipeMessage { Type = PipeMessage.Types.Register, GroupName = groupName }))
8. var writeLock = new SemaphoreSlim(1, 1)

Render task (reads from pipe → Spectre.Console):
    while not cancelled:
        line = await reader.ReadLineAsync(ct)
        if null: break
        msg = JsonSerializer.Deserialize<PipeMessage>(line)
        await writeLock.WaitAsync(ct)
        try:
            switch msg.Type:
                ThinkingShow → AnsiConsole.MarkupLine("[dim grey]  thinking...[/]")
                ThinkingHide → (no-op, thinking is a one-liner)
                StreamChunk  → AnsiConsole.Write(msg.Chunk)
                StreamDone   → AnsiConsole.WriteLine()
                AgentMessage → AnsiConsole.Write(new Rule())
                               if msg.IsFromAgent:
                                   AnsiConsole.Write(new Panel(Markup.Escape(msg.Content))
                                       { Header = new PanelHeader($"[bold green] {msg.Sender} [/]"),
                                         Border = BoxBorder.Rounded, Expand = true })
                               else:
                                   AnsiConsole.MarkupLine($"[bold cyan]{Markup.Escape(msg.Sender)}:[/] ...")
        finally: writeLock.Release()

Input task (stdin → pipe):
    AnsiConsole.MarkupLine("[bold]Honeybadger[/] [dim]— group: {groupName}. Ctrl+C to quit.[/]")
    AnsiConsole.Write(new Rule())
    while not cancelled:
        await writeLock.WaitAsync(ct)
        AnsiConsole.Markup("[bold cyan]you>[/] ")
        writeLock.Release()
        line = await Console.In.ReadLineAsync(ct)
        if null: break
        line = line.Trim(); if empty: continue
        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new PipeMessage { Type = PipeMessage.Types.UserMessage,
                              GroupName = groupName, Content = line, Sender = "user" }))

9. await Task.WhenAny(renderTask, inputTask)
10. cts.Cancel()
11. await Task.WhenAll(renderTask, inputTask) (swallow OperationCanceledException)
```

---

## Step 4 — Modify `Honeybadger.Console`

### Edit: `src/Honeybadger.Console/Program.cs`

- Change: `builder.Services.AddSingleton<IChatFrontend, ConsoleChat>()`
- To:     `builder.Services.AddSingleton<IChatFrontend, NamedPipeChatFrontend>()`
- Add using: `using Honeybadger.Host.Ipc;`
- Remove the startup banner that `ConsoleChat` used to print (it no longer exists in this process)

### Edit: `src/Honeybadger.Console/Honeybadger.Console.csproj`

- Remove: `<PackageReference Include="Spectre.Console" />`

### Delete: `src/Honeybadger.Console/ConsoleChat.cs`

---

## Step 5 — Update solution file

### Edit: `Honeybadger.slnx`

Add the new `Honeybadger.Chat` project alongside the other `src/` projects. The `.slnx` format uses:
```xml
<Project Path="src/Honeybadger.Chat/Honeybadger.Chat.csproj" />
```

---

## Files Summary

| Action | File |
|--------|------|
| New    | `src/Honeybadger.Core/Models/PipeMessage.cs` |
| New    | `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs` |
| New    | `src/Honeybadger.Chat/Honeybadger.Chat.csproj` |
| New    | `src/Honeybadger.Chat/Program.cs` |
| Edit   | `src/Honeybadger.Console/Program.cs` |
| Edit   | `src/Honeybadger.Console/Honeybadger.Console.csproj` |
| Delete | `src/Honeybadger.Console/ConsoleChat.cs` |
| Edit   | `Honeybadger.slnx` |

---

## Verification

```bash
dotnet build Honeybadger.slnx          # 0 errors
dotnet test Honeybadger.slnx           # 36 tests still pass (no test changes)

# Terminal 1 — service
dotnet run --project src/Honeybadger.Console

# Terminal 2 — chat on "main"
dotnet run --project src/Honeybadger.Chat -- --group main

# Terminal 3 — chat on another group
dotnet run --project src/Honeybadger.Chat -- --group work
```

Expected:
- Each terminal sees only its own group's responses
- Kill and relaunch a chat terminal — service keeps running, reconnect works
- Start chat client before service → prints error and exits within 5 seconds
