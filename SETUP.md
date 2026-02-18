# Honeybadger Setup Guide

## Prerequisites

1. **.NET 10 SDK** - Required to build and run the application
2. **GitHub Copilot CLI** - Required for AI model access (or disable AutoStart and use alternative)

## Quick Start

### 1. Install Dependencies

Restore NuGet packages:
```bash
dotnet restore Honeybadger.slnx
```

### 2. Configure Copilot CLI

The app expects the GitHub Copilot CLI to be available. Choose one of these options:

#### Option A: AutoStart (Default)
Ensure `copilot` is in your PATH and supports `--server --port` arguments. The service will start it automatically on port 3100.

#### Option B: Manual Start
If you prefer manual control:

1. Edit `src/Honeybadger.Console/appsettings.json`:
   ```json
   "CopilotCli": {
     "Port": 3100,
     "AutoStart": false
   }
   ```

2. Start the Copilot CLI manually:
   ```bash
   copilot --server --port 3100
   ```

#### Option C: Different Port
Change the port in `appsettings.json` if 3100 is already in use:
```json
"CopilotCli": {
  "Port": 3200,
  "AutoStart": true
}
```

### 3. Build the Solution

```bash
dotnet build Honeybadger.slnx
```

### 4. Run the Application

You need TWO terminals:

**Terminal 1 - Headless Service:**
```bash
dotnet run --project src/Honeybadger.Console
```

Expected output:
```
14:32:01.123 [INF] [] Honeybadger.Console  Honeybadger service starting (headless mode)...
14:32:01.234 [INF] [] AgentRegistry  Registered agent: main (Main Agent) — Model: default, Tools: 3, MCP: 0
14:32:01.235 [INF] [] AgentRegistry  Registered agent: scheduler (Task Scheduler) — Model: default, Tools: 6, MCP: 0
14:32:01.236 [INF] [] AgentRegistry  Agent registry initialized with 2 agent(s)
14:32:01.450 [INF] [] CopilotCliService  Copilot CLI starting on port 3100
14:32:01.670 [INF] [] NamedPipeChatFrontend  Named pipe chat frontend listening on 'honeybadger-chat'
```

**Terminal 2 - Chat Client:**
```bash
dotnet run --project src/Honeybadger.Chat -- --group main
```

Expected output:
```
Honeybadger Chat (group: main)
Connecting to service...
Connected! Type your messages below. Press Ctrl+C to exit.

You:
```

## Visual Studio Setup (Multiple Startup Projects)

If using Visual Studio:

1. Right-click solution in Solution Explorer
2. Select "Configure Startup Projects..."
3. Choose "Multiple startup projects"
4. Set **Honeybadger.Console** to "Start"
5. Set **Honeybadger.Chat** to "Start"
6. Click OK

Now pressing F5 will start both projects.

## Configuration Files

### appsettings.json

Located in `src/Honeybadger.Console/appsettings.json`:

- **Agent.DefaultModel** - Default AI model (claude-sonnet-4.5)
- **Agent.MaxConcurrentAgents** - Max parallel agent executions (3)
- **Agent.ConversationHistoryCount** - Messages to include in context (20)
- **Agent.ScheduledTaskHistoryCount** - Messages for scheduled tasks (10)
- **Agent.CopilotCli.Port** - Copilot CLI port (3100)
- **Agent.CopilotCli.AutoStart** - Auto-start Copilot CLI (true)
- **Database.ConnectionString** - SQLite database location
- **Security.MountAllowlistPath** - Filesystem security rules

### Mount Security (config/mount-allowlist.json)

Controls what file paths agents can access:
- **allowedPaths** - Directories agents can read/write
- **blockedPatterns** - File patterns to always block (credentials, keys, etc.)

### Agent Configurations (config/agents/*.json)

- **main.json** - Router agent (delegates to specialists)
- **scheduler.json** - Task scheduling specialist
- Add more agents by creating additional JSON files

### Soul Files (souls/*.md)

- **main.md** - Router agent personality and guidelines
- **scheduler.md** - Scheduler specialist expertise
- Customize to change agent behavior

## Directory Structure (Auto-Created)

On first run, these directories are created:
- `data/` - SQLite database
- `data/ipc/` - Inter-process communication files
- `logs/` - Log files (honeybadger.log, honeybadger-debug.log)
- `groups/main/` - Main group memory and context
- `config/agents/` - Agent configurations
- `souls/` - Agent soul files

## Troubleshooting

### "Could not connect to Honeybadger service (timeout after 5s)"

The chat client can't connect to the service. Ensure:
1. Honeybadger.Console is running first
2. No firewall blocking named pipe `honeybadger-chat`

### "Copilot CLI failed to start"

If you see Copilot CLI errors:
1. Verify `copilot` is in your PATH: `copilot --version`
2. Try manual start mode (Option B above)
3. Check the port isn't already in use: `netstat -an | findstr 3100`

### "No agent configuration files found"

Agent configs are missing. They should be created automatically, but if not:
1. Check `config/agents/` directory exists
2. Verify `main.json` and `scheduler.json` are present
3. Rebuild the solution to regenerate files

### Database Errors

SQLite database issues:
1. Delete `data/honeybadger.db` to start fresh
2. The service will recreate it on next start

## Testing

Run all tests:
```bash
dotnet test Honeybadger.slnx
```

Run specific test project:
```bash
dotnet test tests/Honeybadger.Host.Tests
```

## Development Tips

### Viewing Logs

Real-time logs appear in the service terminal (colored by level).

File logs:
- `logs/honeybadger.log` - Info level and above
- `logs/honeybadger-debug.log` - All levels including Debug

### Changing Log Levels

Edit `appsettings.json` without recompiling:
```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Debug",  // Change to Warning to reduce noise
    "Override": {
      "Honeybadger": "Information"  // Fine-tune per namespace
    }
  }
}
```

### Multiple Chat Clients

Start multiple chat clients for different groups:
```bash
# Terminal 3
dotnet run --project src/Honeybadger.Chat -- --group work

# Terminal 4
dotnet run --project src/Honeybadger.Chat -- --group personal
```

Each sees only its own group's messages.

## Next Steps

- Read `CLAUDE.md` for architecture details
- Check `plans/00-overview.md` for development roadmap
- Explore agent configurations in `config/agents/`
- Customize soul files in `souls/`
