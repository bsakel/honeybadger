# Plan 09 — Multi-Frontend Chat Support

## Context

Honeybadger has a single `IChatFrontend` implementation (named pipes, local only). This plan adds Telegram as the first external chat platform and a generic pattern for future platforms (Discord, WhatsApp, Slack, etc.). Each group binds to exactly one frontend — "main" stays local-only.

## Design

**Composite pattern**: A `CompositeChatFrontend` implements `IChatFrontend`, holds multiple `IFrontendProvider` backends, merges their inbound channels, and routes outbound messages by group name. `MessageLoopService` stays unchanged — it still sees a single `IChatFrontend`.

```
MessageLoopService (unchanged)
    |
    v
IChatFrontend = CompositeChatFrontend
    |
    +-- "main"           → NamedPipeChatFrontend (local)
    +-- "telegram-home"  → TelegramChatFrontend
    +-- "discord-dev"    → future...
```

## Steps

### Step 1: Add `IFrontendProvider` interface to Core

**New file**: `src/Honeybadger.Core/Interfaces/IFrontendProvider.cs`

```csharp
public interface IFrontendProvider : IChatFrontend
{
    string FrontendType { get; }  // "local", "telegram", etc.
}
```

Extends `IChatFrontend` + adds a type key for routing. No `IAsyncDisposable` requirement — individual implementations opt in.

### Step 2: Add `Frontend` property to `GroupOptions`

**Modify**: `src/Honeybadger.Core/Configuration/GroupOptions.cs`

Add `public string Frontend { get; set; } = "local";` — backward compatible default.

### Step 3: Refactor `NamedPipeChatFrontend` to implement `IFrontendProvider`

**Modify**: `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs`

Change `IChatFrontend, IDisposable` → `IFrontendProvider, IDisposable`. Add `public string FrontendType => "local";`. No other changes.

### Step 4: Create `CompositeChatFrontend`

**New file**: `src/Honeybadger.Host/Frontends/CompositeChatFrontend.cs`

- Takes `IEnumerable<IFrontendProvider>` + `IOptions<HoneybadgerOptions>` via DI
- Builds `Dictionary<string, IFrontendProvider>` from group configs (group name → provider by `Frontend` key)
- Merges all providers' `IncomingMessages` into one `Channel<ChatMessage>` via background tasks
- Routes outbound calls by group name lookup, falls back to local provider
- Implements `IDisposable`/`IAsyncDisposable` to cancel merger tasks and dispose providers

### Step 5: Update DI in `Program.cs`

**Modify**: `src/Honeybadger.Console/Program.cs`

Replace:
```csharp
builder.Services.AddSingleton<IChatFrontend, NamedPipeChatFrontend>();
```
With:
```csharp
builder.Services.AddSingleton<IFrontendProvider, NamedPipeChatFrontend>();
if (builder.Configuration.GetSection("Telegram:BotToken").Exists())
    builder.Services.AddTelegramFrontend(builder.Configuration);
builder.Services.AddSingleton<IChatFrontend, CompositeChatFrontend>();
```

### Step 6: Create `Honeybadger.Telegram` project

**New project**: `src/Honeybadger.Telegram/`

Separate project because it needs `Telegram.Bot` NuGet — follows the folder-vs-project rule.

Files:
- `Honeybadger.Telegram.csproj` — refs `Telegram.Bot` + `Honeybadger.Core`
- `TelegramOptions.cs` — `BotToken`, `GroupChatIds` (dict: group name → chat ID)
- `TelegramChatFrontend.cs` — `IFrontendProvider` with `FrontendType => "telegram"`
  - Long-polling via `Telegram.Bot`
  - Maps chat IDs ↔ group names via config
  - Buffers stream chunks, sends full message on `StreamComplete` (Telegram has no streaming API)
  - `ShowAgentThinking` → `SendChatAction(Typing)`
  - Splits messages > 4096 chars (Telegram limit)
- `ServiceCollectionExtensions.cs` — `AddTelegramFrontend(IConfiguration)`

### Step 7: Update configuration and solution

**Modify**: `src/Honeybadger.Console/appsettings.json` — add `Frontend` to group configs, add `Telegram` section:

```jsonc
{
  "Groups": {
    "main": { "IsMain": true, "Frontend": "local" },
    "telegram-home": { "Frontend": "telegram" }
  },
  "Telegram": {
    "BotToken": "",
    "GroupChatIds": { "telegram-home": 0 }
  }
}
```

Bot token should use `dotnet user-secrets` or env var (`Telegram__BotToken`), never committed.

**Modify**: `Directory.Packages.props` — add `Telegram.Bot` version
**Modify**: `Honeybadger.slnx` — add `Honeybadger.Telegram` project

### Step 7.5: Create Telegram group persona with formatting rules

**New file:** `groups/telegram-home/CLAUDE.md`

```markdown
# Telegram Group

This group communicates via Telegram. Follow these formatting rules strictly:

## Output Formatting
- Keep responses under 300 words — mobile-first, concise
- Use single asterisks for bold: *bold* (not **bold**)
- No headings (# or ##) — Telegram doesn't render them
- No tables — use bullet lists instead
- No code blocks longer than 20 lines — link to a file instead
- Split messages at natural paragraph breaks if over 4096 characters
- Use emoji sparingly and only when contextually appropriate

## Interaction Style
- Assume the user is on a phone — short responses, minimal scrolling
- For complex answers, lead with a one-line summary, then details
- Ask one question at a time, not multiple
```

### Step 8: Add tests

- Test `CompositeChatFrontend` routing with mock `IFrontendProvider` instances
- Test group-to-provider mapping, fallback behavior, channel merging

### Step 9: Update docs

**Modify**: `CLAUDE.md` — add `IFrontendProvider` extensibility pattern (mirrors `IToolProvider` docs), update architecture diagram
**Modify**: `README.md` — mention multi-frontend support, Telegram setup instructions

## Files Changed

| File | Action |
|------|--------|
| `src/Honeybadger.Core/Interfaces/IFrontendProvider.cs` | **New** — frontend provider interface |
| `src/Honeybadger.Core/Configuration/GroupOptions.cs` | Add `Frontend` property |
| `src/Honeybadger.Host/Ipc/NamedPipeChatFrontend.cs` | Implement `IFrontendProvider` |
| `src/Honeybadger.Host/Frontends/CompositeChatFrontend.cs` | **New** — composite router |
| `src/Honeybadger.Telegram/Honeybadger.Telegram.csproj` | **New** — project file |
| `src/Honeybadger.Telegram/TelegramOptions.cs` | **New** — config POCO |
| `src/Honeybadger.Telegram/TelegramChatFrontend.cs` | **New** — Telegram adapter |
| `src/Honeybadger.Telegram/ServiceCollectionExtensions.cs` | **New** — DI extension |
| `src/Honeybadger.Console/Program.cs` | DI registration changes |
| `src/Honeybadger.Console/appsettings.json` | Add Telegram config section |
| `Directory.Packages.props` | Add `Telegram.Bot` package version |
| `Honeybadger.slnx` | Add project reference |
| `groups/telegram-home/CLAUDE.md` | **New** — Telegram group persona with formatting rules |
| `CLAUDE.md` | Document frontend extensibility pattern |
| `README.md` | Update features and setup |

## Verification

```bash
dotnet build Honeybadger.slnx          # 0 errors, 0 warnings
dotnet test Honeybadger.slnx           # all existing + new tests pass
# Manual: run host, connect chat client to "main" — still works as before
```

## Adding a New Frontend (future)

Follow the same pattern as `IToolProvider`:

1. Create `src/Honeybadger.{Platform}/` with `{Platform}ChatFrontend : IFrontendProvider`
2. Add `ServiceCollectionExtensions.cs` with `Add{Platform}Frontend()`
3. Call it from `Program.cs` — the only file that changes
4. Add the group config with `"Frontend": "{platform}"`
