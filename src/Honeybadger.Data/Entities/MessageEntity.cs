namespace Honeybadger.Data.Entities;

public class MessageEntity
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsFromAgent { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ChatEntity Chat { get; set; } = null!;
}
