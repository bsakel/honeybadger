using Honeybadger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Data;

public class HoneybadgerDbContext(DbContextOptions<HoneybadgerDbContext> options) : DbContext(options)
{
    public DbSet<ChatEntity> Chats => Set<ChatEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ScheduledTaskEntity> ScheduledTasks => Set<ScheduledTaskEntity>();
    public DbSet<TaskRunLogEntity> TaskRunLogs => Set<TaskRunLogEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<GroupRegistrationEntity> GroupRegistrations => Set<GroupRegistrationEntity>();
    public DbSet<RouterStateEntity> RouterState => Set<RouterStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChatEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupName).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.GroupName).IsUnique();
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).IsRequired().HasMaxLength(50);
            e.Property(x => x.Sender).IsRequired().HasMaxLength(100);
            e.Property(x => x.Content).IsRequired();
            e.HasIndex(x => new { x.ChatId, x.Timestamp });
            e.HasOne(x => x.Chat)
             .WithMany(x => x.Messages)
             .HasForeignKey(x => x.ChatId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduledTaskEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupName).IsRequired().HasMaxLength(100);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<TaskRunLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Task)
             .WithMany(x => x.RunLogs)
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SessionId).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.ChatId, x.SessionId }).IsUnique();
            e.HasOne(x => x.Chat)
             .WithMany(x => x.Sessions)
             .HasForeignKey(x => x.ChatId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupRegistrationEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupName).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.GroupName).IsUnique();
        });

        modelBuilder.Entity<RouterStateEntity>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).IsRequired().HasMaxLength(200);
            e.Property(x => x.Value).IsRequired();
        });
    }
}
