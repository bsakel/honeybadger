using Honeybadger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Data.Repositories;

public class TaskRepository(HoneybadgerDbContext db)
{
    public async Task<ScheduledTaskEntity> AddAsync(ScheduledTaskEntity task, CancellationToken ct = default)
    {
        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public Task<ScheduledTaskEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        => db.ScheduledTasks.FindAsync([id], ct).AsTask();

    public async Task<IReadOnlyList<ScheduledTaskEntity>> GetByGroupAsync(string groupName, CancellationToken ct = default)
        => await db.ScheduledTasks
            .Where(t => t.GroupName == groupName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ScheduledTaskEntity>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // Load active tasks with a NextRunAt set, then compare dates in-memory.
        // DateTimeOffset comparisons are not translatable on all providers (e.g. SQLite).
        var candidates = await db.ScheduledTasks
            .Where(t => t.Status == TaskStatusData.Active && t.NextRunAt != null)
            .ToListAsync(ct);
        return candidates.Where(t => t.NextRunAt!.Value <= now).ToList();
    }

    public async Task UpdateStatusAsync(int id, TaskStatusData status, CancellationToken ct = default)
    {
        await db.ScheduledTasks
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), ct);
    }

    public async Task UpdateAfterRunAsync(
        int id,
        DateTimeOffset lastRunAt,
        DateTimeOffset? nextRunAt,
        TaskStatusData status,
        CancellationToken ct = default)
    {
        await db.ScheduledTasks
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.LastRunAt, lastRunAt)
                .SetProperty(t => t.NextRunAt, nextRunAt)
                .SetProperty(t => t.Status, status), ct);
    }

    public async Task AddRunLogAsync(TaskRunLogEntity log, CancellationToken ct = default)
    {
        db.TaskRunLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }
}
