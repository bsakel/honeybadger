namespace Honeybadger.Core.Configuration;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public string DefaultModel { get; set; } = "claude-sonnet-4.5";
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxConcurrentAgents { get; set; } = 3;
    public int ConversationHistoryCount { get; set; } = 20;
    public CopilotCliOptions CopilotCli { get; set; } = new();
}

public class CopilotCliOptions
{
    public int Port { get; set; } = 3100;
    public bool AutoStart { get; set; } = true;
    /// <summary>Path to the Copilot CLI executable.</summary>
    public string ExecutablePath { get; set; } = "copilot";
    /// <summary>Arguments to pass to start the CLI in network/server mode.</summary>
    public string Arguments { get; set; } = "--server --port {port}";
}
