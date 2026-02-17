namespace Honeybadger.Data.Entities;

public class SessionEntity
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChatEntity Chat { get; set; } = null!;
}
