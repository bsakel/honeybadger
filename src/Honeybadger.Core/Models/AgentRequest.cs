namespace Honeybadger.Core.Models;

public record AgentRequest
{
    public string CorrelationId { get; init; } = string.Empty;
    public string MessageId { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string? Model { get; init; }
    public string? GlobalMemory { get; init; }
    public string? GroupMemory { get; init; }
    public string? AgentMemory { get; init; }        // From MEMORY.md (Phase 6B)
    public string? ConversationSummary { get; init; } // From summary.md (Phase 6B)
    public string? ConversationHistory { get; init; }
    public string CopilotCliEndpoint { get; init; } = string.Empty;

    // Multi-agent fields (Phase 4+)
    public string? AgentId { get; init; }
    public bool IsRouterAgent { get; init; }
    public string? Soul { get; init; }
    public List<string> AvailableTools { get; init; } = [];
}
