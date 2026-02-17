namespace Honeybadger.Data.Entities;

public class ChatEntity
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActivityAt { get; set; }

    public ICollection<MessageEntity> Messages { get; set; } = [];
    public ICollection<SessionEntity> Sessions { get; set; } = [];
}
