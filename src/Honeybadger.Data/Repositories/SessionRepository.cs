using Honeybadger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Data.Repositories;

public class SessionRepository(HoneybadgerDbContext db)
{
    public async Task<SessionEntity?> GetLatestAsync(string groupName, CancellationToken ct = default)
        => await db.Sessions
            .Include(s => s.Chat)
            .Where(s => s.Chat.GroupName == groupName)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<SessionEntity> UpsertAsync(
        string groupName, string sessionId, CancellationToken ct = default)
    {
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.GroupName == groupName, ct);
        if (chat is null)
        {
            chat = new ChatEntity { GroupName = groupName };
            db.Chats.Add(chat);
            await db.SaveChangesAsync(ct);
        }

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.ChatId == chat.Id && s.SessionId == sessionId, ct);

        if (session is null)
        {
            session = new SessionEntity { ChatId = chat.Id, SessionId = sessionId };
            db.Sessions.Add(session);
        }
        else
        {
            session.LastUsedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return session;
    }
}
