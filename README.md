# 🍯 Honeybadger

> A lightweight in-process personal AI assistant built in C#/.NET 10, inspired by [nanoclaw](https://github.com/gavrielc/nanoclaw)

Honeybadger is an AI-powered assistant that runs agents in-process, uses the GitHub Copilot SDK for intelligence, and provides a rich console interface via Spectre.Console. It supports scheduling tasks (cron, interval, once), conversational memory, and file-based IPC for tool execution.

## ✨ Features

- **🤖 Intelligent Agents** — Powered by GitHub Copilot SDK with customizable models per group
- **⚡ In-Process Execution** — Agents run directly in the host process for simplicity and speed
- **💬 Rich Console UI** — Beautiful terminal interface with streaming token output
- **⏰ Task Scheduling** — Cron expressions, intervals, and one-time tasks
- **🧠 Conversation Memory** — Automatic context from recent messages + hierarchical CLAUDE.md files
- **🔒 Security First** — Mount allowlisting, symlink resolution, validated tool execution
- **📊 SQLite Database** — Simple file-based persistence with EF Core
- **🚀 Streaming Responses** — Real-time token-by-token output as the agent thinks
- **🔧 Custom Tools** — Agents can send messages, schedule tasks, list tasks via IPC
- **✅ GitHub Actions CI** — Automated build, test, and lint

## How It Works

```
You type in the console
        |
        v
  MessageLoopService routes your message through the GroupQueue
        |
        v
  LocalAgentRunner creates AgentOrchestrator in-process
        |
        v
  Agent connects to the host's Copilot CLI (port 3100)
  and runs a CopilotSession with your message + tools
        |
        v
  Agent response streams back in real-time; IPC commands
  (schedule_task, send_message, etc.) written as JSON files
        |
        v
  Response displayed in the console via Spectre.Console
```

Each group (conversation) has serialized message processing. The agent has six IPC tools it can call: `send_message`, `schedule_task`, `pause_task`, `resume_task`, `cancel_task`, `list_tasks`. Scheduled tasks run on cron expressions, fixed intervals, or as one-shots.

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [GitHub Copilot CLI](https://githubnext.com/projects/copilot-cli/) (optional, for AI features)

### Installation

1. Clone the repository:
```bash
git clone https://github.com/bsakel/honeybadger.git
cd honeybadger
```

2. Build the solution:
```bash
dotnet build Honeybadger.slnx
```

3. Run tests to verify everything works:
```bash
dotnet test Honeybadger.slnx
```

### Running

```bash
cd src/Honeybadger.Console
dotnet run
```

## 📝 Configuration

Edit `src/Honeybadger.Console/appsettings.json`:

```jsonc
{
  "Agent": {
    "DefaultModel": "claude-sonnet-4.5",      // AI model to use
    "MaxConcurrentAgents": 3,                 // Max parallel agents
    "ConversationHistoryCount": 20,           // Recent messages to include
    "CopilotCli": {
      "Port": 3100,
      "AutoStart": true,                      // Auto-start Copilot CLI
      "ExecutablePath": "copilot",
      "Arguments": "--server --port {port}"
    }
  },
  "Groups": {
    "main": {
      "Model": null,                          // Override model for this group
      "IsMain": true
    }
  },
  "Database": {
    "ConnectionString": "Data Source=data/honeybadger.db"
  }
}
```

## 🎯 Usage

### Interactive Console

Start the console and type your messages:

```
You: What's the weather like today?
Agent: I'll check that for you...
[Streaming response appears in real-time]
```

### Scheduling Tasks

Agents can schedule recurring or one-time tasks:

```
You: Schedule a daily standup reminder at 9 AM on weekdays
Agent: [Uses schedule_task tool]
Task 'Daily Standup' scheduled (cron: 0 9 * * MON-FRI)
```

### Listing Tasks

```
You: What tasks do I have scheduled?
Agent: [Uses list_tasks tool]
Found 2 scheduled task(s):
- ID 1: Daily Standup (Cron, Active)
  Next run: 2026-02-09T09:00:00Z
- ID 2: Weekly Report (Interval, Active)
  Next run: 2026-02-08T14:30:00Z
```

### Conversation Context

The agent automatically remembers recent conversation history:

```
You: My favorite color is blue
Agent: Got it! Blue is a great choice.

[Later...]
You: What's my favorite color?
Agent: You told me earlier that your favorite color is blue.
```

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────┐
│                    HOST PROCESS (.NET)                  │
│                                                         │
│  ConsoleChat (Spectre.Console)                          │
│       │                                                 │
│       ▼                                                 │
│  MessageLoopService                                     │
│       │                                                 │
│       ├─ GroupQueue (per-group serialization)           │
│       │                                                 │
│       ▼                                                 │
│  LocalAgentRunner (in-process)                          │
│       │                                                 │
│       ▼                                                 │
│  AgentOrchestrator                                      │
│    ├─ CopilotClient                                     │
│    ├─ CopilotSession (streaming)                        │
│    └─ IpcTools                                          │
│       ├─ send_message                                   │
│       ├─ schedule_task                                  │
│       ├─ pause_task                                     │
│       ├─ resume_task                                    │
│       ├─ cancel_task                                    │
│       └─ list_tasks                                     │
│                                                         │
│  IpcWatcherService (watches data/ipc/)                  │
│  SchedulerService (cron/interval/once)                  │
│  CopilotCliService (SDK-managed, port 3100)             │
│  EF Core (SQLite)                                       │
└────────────────────────────────────────────────────────┘
```

## 🗂️ Project Structure

```
honeybadger/
├── src/
│   ├── Honeybadger.Core/           # Shared models, interfaces, config
│   ├── Honeybadger.Data/           # EF Core DbContext
│   ├── Honeybadger.Data.Sqlite/    # SQLite provider + migrations
│   ├── Honeybadger.Host/           # Host orchestration services
│   ├── Honeybadger.Agent/          # Agent logic (runs in-process)
│   └── Honeybadger.Console/        # Console frontend (entry point)
├── tests/                          # 36 tests across 4 projects
├── groups/                         # Per-group CLAUDE.md memory files
├── config/                         # mount-allowlist.json
└── .github/workflows/              # CI pipeline
```

## 🧪 Testing

Run all tests:
```bash
dotnet test Honeybadger.slnx
```

Run specific test project:
```bash
dotnet test tests/Honeybadger.Host.Tests
```

Run tests with coverage:
```bash
dotnet test Honeybadger.slnx /p:CollectCoverage=true
```

## 🛠️ Development

### Adding New Agent Tools

1. Add tool method to `src/Honeybadger.Agent/Tools/IpcTools.cs`:
```csharp
private async Task<string> MyNewTool(string param)
{
    await WriteIpcFileAsync(IpcMessageType.MyCommand, new MyPayload { Param = param });
    return "Success";
}
```

2. Register in `GetAll()`:
```csharp
AIFunctionFactory.Create(MyNewTool, "my_new_tool", "Description")
```

3. Add handler in `src/Honeybadger.Host/Services/IpcWatcherService.cs`

### Database Migrations

Add a new migration:
```bash
dotnet ef migrations add MigrationName \
  --project src/Honeybadger.Data.Sqlite \
  --startup-project src/Honeybadger.Console
```

Apply migrations:
```bash
dotnet ef database update \
  --project src/Honeybadger.Data.Sqlite \
  --startup-project src/Honeybadger.Console
```


## 🔒 Security

### Mount Allowlist

Edit `config/mount-allowlist.json` to control which directories agents can access:
```json
{
  "allowedPaths": [
    "groups/",
    "data/",
    "C:\\safe\\project\\path"
  ]
}
```

Blocked patterns (hardcoded for security):
- `/etc`, `/sys`, `/proc`, `/root`
- `.ssh/`, `.aws/`, `.env`
- Anything outside the allowlist

Symlinks are resolved and validated against the allowlist.

## 📚 Documentation

- **CLAUDE.md** — Detailed technical documentation for Claude Code
- **README.md** — This file (user-facing documentation)
- Inline code comments throughout the codebase

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Ensure all tests pass before submitting:
```bash
dotnet build Honeybadger.slnx
dotnet test Honeybadger.slnx
```

## 📜 License

MIT License - see [LICENSE](LICENSE) for details

## 🙏 Acknowledgments

- Inspired by [nanoclaw](https://github.com/gavrielc/nanoclaw) by [@gavrielc](https://github.com/gavrielc)
- Built with [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
- UI powered by [Spectre.Console](https://spectreconsole.net/)

## 🐛 Known Issues & Roadmap

### Current Limitations
- Console-only interface (no WhatsApp/Telegram/Web UI yet)
- Single-process architecture (no isolation between groups)
- SQLite only (no SQL Server or other databases)

### Roadmap
- [ ] WhatsApp frontend via `IChatFrontend`
- [ ] Web UI (Blazor or ASP.NET Core)
- [ ] Docker containerization for agent sandboxing (optional)
- [ ] SQL Server support for scale-out scenarios (optional)
- [ ] Multi-tenant support
- [ ] Cursor-based message replay for crash recovery

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/bsakel/honeybadger/issues)
- **Discussions**: [GitHub Discussions](https://github.com/bsakel/honeybadger/discussions)

---

Made with ❤️ by the Honeybadger team
