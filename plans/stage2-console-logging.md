# Stage 2 — Console Logging in the Service

## Goal

The service terminal (`Honeybadger.Console`) now runs headlessly — no Spectre.Console UI competing for stdout. Add a Serilog console sink so all log output (from all in-process components: services, agent, IPC, scheduler) is visible in that terminal in real time, colored by level. The minimum level on the console sink is configurable in `appsettings.json` without recompiling.

**Prerequisite:** Stage 1 complete.

---

## Why appsettings-driven sinks

`Serilog.Settings.Configuration` is already wired (`ReadFrom.Configuration(builder.Configuration)` in `Program.cs`). Moving the sink declarations into `appsettings.json` means all three sinks (console + two file sinks) are adjustable at runtime by editing JSON. The Serilog setup in code shrinks to two lines.

---

## Step 1 — Add package reference

### Edit: `src/Honeybadger.Console/Honeybadger.Console.csproj`

`Serilog.Sinks.Console` version `6.0.0` is already in `Directory.Packages.props`. Just add the reference:

```xml
<PackageReference Include="Serilog.Sinks.Console" />
```

(Alongside the existing `Serilog.Sinks.File` reference.)

---

## Step 2 — Move sinks to `appsettings.json`

### Edit: `src/Honeybadger.Console/appsettings.json`

Replace the current `"Serilog"` section (which only has `MinimumLevel`) with the full version below. This adds:
- `"Using"` — tells `Serilog.Settings.Configuration` which sink assemblies to load
- `"WriteTo"` — declares all three sinks with their args
- `"Enrich"` — moves `FromLogContext` from code into config

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

To quieten the console without recompiling:
- Change `"restrictedToMinimumLevel"` on the Console sink to `"Information"` or `"Warning"`

---

## Step 3 — Simplify `Program.cs` logging setup

### Edit: `src/Honeybadger.Console/Program.cs`

The current Serilog block is:

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/honeybadger.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "...")
    .WriteTo.File("logs/honeybadger-debug.log",
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Debug,
        outputTemplate: "...")
    .CreateLogger();
```

Replace it with:

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
```

The sinks and enricher are now fully declared in `appsettings.json`. The `using Serilog.Events;` import can be removed if nothing else uses `LogEventLevel`.

---

## Files Summary

| Action | File |
|--------|------|
| Edit   | `src/Honeybadger.Console/Honeybadger.Console.csproj` |
| Edit   | `src/Honeybadger.Console/appsettings.json` |
| Edit   | `src/Honeybadger.Console/Program.cs` |

---

## Verification

```bash
dotnet build Honeybadger.slnx    # 0 errors
dotnet test Honeybadger.slnx     # 36 tests still pass
dotnet run --project src/Honeybadger.Console
```

Expected console output (colored by level):
```
14:32:01.123 [INF] [] Honeybadger.Console  Honeybadger service started. Waiting for chat clients...
14:32:01.450 [INF] [] CopilotCliService    Copilot CLI starting on port 3100
14:32:05.441 [DBG] [a1b2c3] MessageLoopService  Processing message from user (12 chars)
14:32:05.902 [WRN] [] NamedPipeChatFrontend  No chat client connected for group 'work'
```

File sinks continue working as before (unchanged behavior, just declared in config now).
