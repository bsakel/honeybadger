# Phase 3 — Console Logging in the Service

## Goal

The service terminal (`Honeybadger.Console`) now runs headlessly — no Spectre.Console competing for stdout. Add a Serilog console sink so all log output is visible in that terminal in real time, colored by level. Move all sink declarations to `appsettings.json` for runtime configurability.

**Prerequisite:** Phase 2 complete (headless service)

---

## Step 1 — Add Package Reference

**Edit: `src/Honeybadger.Console/Honeybadger.Console.csproj`**

Add alongside existing packages:
```xml
<PackageReference Include="Serilog.Sinks.Console" />
```

Version is already in `Directory.Packages.props`.

---

## Step 2 — Move Sinks to appsettings.json

**Edit: `src/Honeybadger.Console/appsettings.json`**

Replace the current `"Serilog"` section with:

```json
"Serilog": {
  "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "System": "Warning",
      "Honeybadger": "Debug"
    }
  },
  "WriteTo": [
    {
      "Name": "Console",
      "Args": {
        "theme": "Serilog.Sinks.SystemConsole.Themes.SystemConsoleTheme::Colored, Serilog.Sinks.Console",
        "outputTemplate": "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        "restrictedToMinimumLevel": "Debug"
      }
    },
    {
      "Name": "File",
      "Args": {
        "path": "logs/honeybadger.log",
        "rollingInterval": "Day",
        "outputTemplate": "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}"
      }
    },
    {
      "Name": "File",
      "Args": {
        "path": "logs/honeybadger-debug.log",
        "rollingInterval": "Day",
        "restrictedToMinimumLevel": "Debug",
        "outputTemplate": "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}"
      }
    }
  ],
  "Enrich": ["FromLogContext"]
}
```

---

## Step 3 — Simplify Program.cs Logging Setup

**Edit: `src/Honeybadger.Console/Program.cs`**

Replace lines 23-33 (current Serilog block):
```csharp
// Before:
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/honeybadger.log", ...)
    .WriteTo.File("logs/honeybadger-debug.log", ...)
    .CreateLogger();
```

With:
```csharp
// After:
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
```

Remove `using Serilog.Events;` if nothing else uses `LogEventLevel`.

---

## Files Summary

| Action | File |
|--------|------|
| Edit | `src/Honeybadger.Console/Honeybadger.Console.csproj` |
| Edit | `src/Honeybadger.Console/appsettings.json` |
| Edit | `src/Honeybadger.Console/Program.cs` |

---

## Test Plan

### Automated Tests

No new tests needed — this is a configuration-only change.

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # All tests pass
```

### Manual Verification

```bash
dotnet run --project src/Honeybadger.Console
```

Expected colored console output:
```
14:32:01.123 [INF] [] Honeybadger.Console  Honeybadger starting...
14:32:01.450 [INF] [] CopilotCliService    Copilot CLI starting on port 3100
14:32:05.441 [DBG] [a1b2c3] MessageLoopService  Processing message from user (12 chars)
14:32:05.902 [WRN] [] NamedPipeChatFrontend  No chat client connected for group 'work'
```

To quieten the console without recompiling: change `"restrictedToMinimumLevel"` on the Console sink to `"Information"` or `"Warning"` in `appsettings.json`.

File sinks continue working unchanged (same behavior, just declared in config now).
