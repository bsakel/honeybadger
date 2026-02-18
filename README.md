# 🍯 Honeybadger

> A lightweight in-process personal AI assistant built in C#/.NET 10, inspired by [nanoclaw](https://github.com/gavrielc/nanoclaw)

Honeybadger is an AI-powered assistant that runs agents in-process, uses the GitHub Copilot SDK for intelligence, and provides a rich console interface via Spectre.Console. It supports scheduling tasks (cron, interval, once), conversational memory, and file-based IPC for tool execution.

## ✨ Features

- **🤖 Multi-Agent Collaboration** — Router agents delegate tasks to specialist agents with custom tools and personalities
- **⚡ In-Process Execution** — Agents run directly in the host process for simplicity and speed
- **💬 Named-Pipe UI** — Headless service + separate chat client for cleaner architecture
- **⏰ Task Scheduling** — Cron expressions, intervals, and one-time tasks
- **🧠 Enhanced Memory System** — Three-tier memory (persona/facts/summaries) with agent-writable persistence
- **📝 update_memory Tool** — Agents can save learned facts for future conversations
- **🎯 Token Budget Awareness** — Configurable token budget (8000 default) prioritizes recent messages
- **🔒 Security First** — Mount allowlisting, symlink resolution, validated tool execution
- **📊 SQLite Database** — Simple file-based persistence with EF Core
- **🚀 Streaming Responses** — Real-time token-by-token output as the agent thinks
- **🔧 Dynamic Tools** — Tools configured per agent; IPC, delegation, memory, and scheduling
- **✅ Comprehensive Testing** — 44 tests (7 Core + 16 Integration + 21 Host)

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

Each group (conversation) has serialized message processing. Agents can be routers (delegate to specialists) or specialists (handle specific tasks). Tools are configured per agent and can include:
- **IPC Tools**: `send_message`, `schedule_task`, `pause_task`, `resume_task`, `cancel_task`, `list_tasks`, `update_memory`
- **Delegation Tools**: `delegate_to_agent`, `list_available_agents` (router agents only)

Agents have access to a three-tier memory system:
- **CLAUDE.md** — Persona (read-only, defines character/role)
- **MEMORY.md** — Learned facts (agent-writable via update_memory tool)
- **summary.md** — Conversation summaries (future feature)

Token budget (default 8000) ensures conversation history fits in context window by prioritizing recent messages.

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
    "DefaultModel": "claude-sonnet-4.5",            // AI model to use
    "MaxConcurrentAgents": 3,                       // Max parallel agents
    "ConversationHistoryCount": 20,                 // Recent messages to include
    "ConversationHistoryTokenBudget": 8000,         // Token limit for history (0 = unlimited)
    "ScheduledTaskHistoryCount": 10,                // History for scheduled tasks
    "CopilotCli": {
      "Port": 3100,
      "AutoStart": true,                            // Auto-start Copilot CLI
      "ExecutablePath": "copilot",
      "Arguments": "--server --port {port}"
    }
  },
  "Groups": {
    "main": {
      "Model": null,                                // Override model for this group
      "IsMain": true
    }
  },
  "Database": {
    "ConnectionString": "Data Source=data/honeybadger.db"
  }
}
```

### Agent Configuration

Create agent configs in `config/agents/`:

**Router Agent** (`config/agents/main.json`):
```json
{
  "agentId": "main",
  "name": "Main Agent",
  "description": "Primary orchestrator that analyzes requests and delegates to specialists",
  "soul": "souls/main.md",
  "tools": ["delegate_to_agent", "send_message", "list_available_agents", "update_memory"],
  "isRouter": true
}
```

**Specialist Agent** (`config/agents/scheduler.json`):
```json
{
  "agentId": "scheduler",
  "name": "Scheduler Agent",
  "description": "Manages scheduled tasks and reminders",
  "soul": "souls/scheduler.md",
  "model": "claude-sonnet-4.5",
  "tools": ["schedule_task", "list_tasks", "pause_task", "resume_task", "cancel_task"],
  "isRouter": false
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

### Multi-Agent Collaboration

Router agents can delegate to specialists:

```
You: Schedule a daily standup at 9 AM and remind me about my favorite color
Agent (Router): I'll delegate the scheduling task to the scheduler specialist...
Agent (Scheduler): [Uses schedule_task tool]
Agent (Router): Task scheduled! And I remember your favorite color is blue.
```

### Agent Memory Persistence

Agents can save facts for future sessions:

```
You: Remember that I prefer Python for scripting
Agent: [Uses update_memory tool]
Memory updated

[Later, in a new session...]
You: What's my preferred scripting language?
Agent: According to my notes, you prefer Python for scripting.
```

Memory is stored in `groups/{groupName}/MEMORY.md` with attribution:

```markdown
## Preferences (main, 2026-02-18 14:30)
- User prefers Python for scripting
```

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────┐
│                    HOST PROCESS (.NET)                  │
│                                                         │
│  NamedPipeChatFrontend (headless service)               │
│       │                                                 │
│       ▼                                                 │
│  MessageLoopService                                     │
│    ├─ AgentRegistry (loads config/agents/*.json)        │
│    ├─ AgentToolFactory (maps config → tools)            │
│    └─ GroupQueue (per-group serialization)              │
│       │                                                 │
│       ▼                                                 │
│  LocalAgentRunner (in-process)                          │
│       │                                                 │
│       ▼                                                 │
│  AgentOrchestrator (with soul file)                     │
│    ├─ CopilotClient (Copilot CLI on port 3100)         │
│    ├─ CopilotSession (streaming responses)              │
│    └─ Dynamic Tools (per agent)                         │
│       ├─ IpcTools (send_message, schedule_task, etc)    │
│       ├─ AgentDelegationTools (delegate, list agents)   │
│       └─ update_memory (writes to MEMORY.md)            │
│                                                         │
│  IpcWatcherService (watches data/ipc/)                  │
│    ├─ Routes delegation requests to specialists         │
│    └─ Handles update_memory writes                      │
│  SchedulerService (cron/interval/once tasks)            │
│  HierarchicalMemoryStore (CLAUDE.md + MEMORY.md cache)  │
│  CopilotCliService (SDK-managed CLI)                    │
│  EF Core (SQLite database)                              │
└────────────────────────────────────────────────────────┘

CHAT CLIENT (separate process)
  ├─ Connects via named pipe "honeybadger-chat"
  ├─ Sends messages (NDJSON protocol)
  └─ Receives responses + streaming chunks
```

## 🗂️ Project Structure

```
honeybadger/
├── src/
│   ├── Honeybadger.Core/           # Shared models, interfaces, config
│   ├── Honeybadger.Data/           # EF Core DbContext
│   ├── Honeybadger.Data.Sqlite/    # SQLite provider + migrations
│   ├── Honeybadger.Host/           # Host orchestration services
│   │   ├── Agents/                 # AgentRegistry, AgentToolFactory, LocalAgentRunner
│   │   ├── Memory/                 # HierarchicalMemoryStore (caching)
│   │   └── Services/               # MessageLoop, IpcWatcher, Scheduler
│   ├── Honeybadger.Agent/          # Agent logic (runs in-process)
│   │   └── Tools/                  # IpcTools, AgentDelegationTools
│   ├── Honeybadger.Console/        # Headless service (entry point)
│   └── Honeybadger.Chat/           # Chat client (named-pipe)
├── tests/                          # 44 tests (7 Core + 16 Integration + 21 Host)
├── config/
│   ├── agents/                     # Agent configurations (*.json)
│   └── mount-allowlist.json        # Filesystem security
├── souls/                          # Agent personality files (*.md)
├── groups/                         # Per-group memory files
│   └── {groupName}/
│       ├── CLAUDE.md               # Persona (read-only)
│       ├── MEMORY.md               # Learned facts (agent-writable)
│       └── summary.md              # Summaries (future)
├── plans/                          # Implementation roadmap
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
- Named-pipe UI only (no WhatsApp/Telegram/Web UI yet)
- Single-process architecture (no isolation between groups)
- SQLite only (no SQL Server or other databases)
- No conversation summarization yet (summary.md files not auto-generated)

### Completed ✅
- ✅ Multi-agent collaboration (router + specialists)
- ✅ Agent-writable memory (update_memory tool)
- ✅ Token budget awareness
- ✅ Separate memory files (persona/facts/summaries)
- ✅ Named-pipe architecture (headless service + chat client)
- ✅ Comprehensive test suite (44 tests)

### Roadmap
- [ ] **Phase 7: Advanced Memory** — Conversation summarization, semantic search with sqlite-vec
- [ ] WhatsApp frontend via `IChatFrontend`
- [ ] Web UI (Blazor or ASP.NET Core)
- [ ] Docker containerization for agent sandboxing (optional)
- [ ] SQL Server support for scale-out scenarios (optional)
- [ ] Multi-tenant support

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/bsakel/honeybadger/issues)
- **Discussions**: [GitHub Discussions](https://github.com/bsakel/honeybadger/discussions)

---

Made with ❤️ by the Honeybadger team
