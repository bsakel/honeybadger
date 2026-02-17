using Honeybadger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Data.Repositories;

public class GroupRepository(HoneybadgerDbContext db)
{
    public async Task<IReadOnlyList<GroupRegistrationEntity>> GetAllAsync(CancellationToken ct = default)
        => await db.GroupRegistrations.ToListAsync(ct);

    public Task<GroupRegistrationEntity?> GetByNameAsync(string groupName, CancellationToken ct = default)
        => db.GroupRegistrations.FirstOrDefaultAsync(g => g.GroupName == groupName, ct);

    public async Task<GroupRegistrationEntity> UpsertAsync(GroupRegistrationEntity group, CancellationToken ct = default)
    {
        var existing = await GetByNameAsync(group.GroupName, ct);
        if (existing is null)
        {
            db.GroupRegistrations.Add(group);
        }
        else
        {
            existing.FolderPath = group.FolderPath;
            existing.TriggerPattern = group.TriggerPattern;
            existing.IsMain = group.IsMain;
        }
        await db.SaveChangesAsync(ct);
        return existing ?? group;
    }

    public async Task<string?> GetRouterStateAsync(string key, CancellationToken ct = default)
    {
        var state = await db.RouterState.FindAsync([key], ct);
        return state?.Value;
    }

    public async Task SetRouterStateAsync(string key, string value, CancellationToken ct = default)
    {
        var state = await db.RouterState.FindAsync([key], ct);
        if (state is null)
        {
            db.RouterState.Add(new RouterStateEntity { Key = key, Value = value });
        }
        else
        {
            state.Value = value;
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
