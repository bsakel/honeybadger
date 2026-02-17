namespace Honeybadger.Core.Models;

public class AgentResponse
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? SessionId { get; init; }
    public string? Error { get; init; }
}
