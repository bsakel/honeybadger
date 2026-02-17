using Honeybadger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Data.Repositories;

public class MessageRepository(HoneybadgerDbContext db)
{
    public async Task<ChatEntity> GetOrCreateChatAsync(string groupName, CancellationToken ct = default)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.GroupName == groupName, ct);
        if (chat is not null) return chat;

        chat = new ChatEntity { GroupName = groupName, CreatedAt = DateTimeOffset.UtcNow };
        db.Chats.Add(chat);
        await db.SaveChangesAsync(ct);
        return chat;
    }

    public async Task<MessageEntity> AddMessageAsync(
        string groupName, string externalId, string sender, string content, bool isFromAgent,
        CancellationToken ct = default)
    {
        var chat = await GetOrCreateChatAsync(groupName, ct);
        var msg = new MessageEntity
        {
            ChatId = chat.Id,
            ExternalId = externalId,
            Sender = sender,
            Content = content,
            IsFromAgent = isFromAgent,
            Timestamp = DateTimeOffset.UtcNow
        };
        db.Messages.Add(msg);
        chat.LastActivityAt = msg.Timestamp;
        await db.SaveChangesAsync(ct);
        return msg;
    }

    public async Task<IReadOnlyList<MessageEntity>> GetRecentMessagesAsync(
        string groupName, int count = 50, CancellationToken ct = default)
    {
        var messages = await db.Messages
            .Include(m => m.Chat)
            .Where(m => m.Chat.GroupName == groupName)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .ToListAsync(ct);

        // Sort chronologically client-side (SQLite can't ORDER BY DateTimeOffset)
        messages.Reverse();
        return messages;
    }
}
