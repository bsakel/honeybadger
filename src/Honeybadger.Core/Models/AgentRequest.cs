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
    public string? ConversationHistory { get; init; }
    public string CopilotCliEndpoint { get; init; } = string.Empty;
}
