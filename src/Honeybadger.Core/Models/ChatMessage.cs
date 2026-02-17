namespace Honeybadger.Core.Models;

public class ChatMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string GroupName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public bool IsFromAgent { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}
