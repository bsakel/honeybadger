using FluentAssertions;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data;
using Honeybadger.Data.Entities;
using Honeybadger.Data.Repositories;
using Honeybadger.Data.Sqlite;
using Honeybadger.Host.Memory;
using Honeybadger.Host.Scheduling;
using Honeybadger.Host.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Honeybadger.Host.Tests.Services;

public class SchedulerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _sp;

    public SchedulerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hb-sched-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // Use SQLite in-memory — supports ExecuteUpdateAsync unlike EF InMemory
        var services = new ServiceCollection();
        services.AddHoneybadgerSqlite($"Data Source={Path.Combine(_tempDir, "test.db")}");
        services.AddScoped<TaskRepository>();
        _sp = services.BuildServiceProvider();

        // Create schema
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoneybadgerDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task DueTask_IsExecuted_AndRunLogCreated()
    {
        // Arrange: add a due "once" task
        using (var scope = _sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
            await repo.AddAsync(new ScheduledTaskEntity
            {
                GroupName = "main",
                Name = "Test task",
                Description = "Do something",
                ScheduleType = ScheduleTypeData.Once,
                Status = TaskStatusData.Active,
                NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        }

        var agentRunnerMock = new Mock<IAgentRunner>();
        agentRunnerMock
            .Setup(c => c.RunAgentAsync(It.IsAny<AgentRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { Success = true, Content = "Done" });

        var cronEval = new CronExpressionEvaluator(NullLogger<CronExpressionEvaluator>.Instance);
        var memoryStore = new HierarchicalMemoryStore(_tempDir, NullLogger<HierarchicalMemoryStore>.Instance);

        var svc = new SchedulerService(
            agentRunnerMock.Object,
            cronEval,
            memoryStore,
            _sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AgentOptions()),
            Options.Create(new HoneybadgerOptions()),
            NullLogger<SchedulerService>.Instance);

        // Act: invoke private TickAsync via reflection to avoid the 30s timer
        var tickMethod = typeof(SchedulerService).GetMethod("TickAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)tickMethod.Invoke(svc, [CancellationToken.None])!;

        // Assert: agent was called with the task content
        agentRunnerMock.Verify(c => c.RunAgentAsync(
            It.Is<AgentRequest>(r => r.GroupName == "main" && r.Content.Contains("Test task")),
            It.IsAny<Func<string, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: run log was created and task marked completed
        using var verifyScope = _sp.CreateScope();
        var taskRepo = verifyScope.ServiceProvider.GetRequiredService<TaskRepository>();
        var task = await taskRepo.GetByIdAsync(1);
        task!.Status.Should().Be(TaskStatusData.Completed);
        task.LastRunAt.Should().NotBeNull();

        var db = verifyScope.ServiceProvider.GetRequiredService<HoneybadgerDbContext>();
        var logs = await db.TaskRunLogs.ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Status.Should().Be(RunStatus.Success);
        logs[0].Result.Should().Be("Done");
    }

    [Fact]
    public async Task IntervalTask_ComputesNextRunAt_AfterExecution()
    {
        const int intervalSeconds = 60;
        using (var scope = _sp.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<TaskRepository>();
            await repo.AddAsync(new ScheduledTaskEntity
            {
                GroupName = "main",
                Name = "Interval task",
                ScheduleType = ScheduleTypeData.Interval,
                Status = TaskStatusData.Active,
                IntervalTicks = TimeSpan.FromSeconds(intervalSeconds).Ticks,
                NextRunAt = DateTimeOffset.UtcNow.AddSeconds(-1)
            });
        }

        var agentRunnerMock = new Mock<IAgentRunner>();
        agentRunnerMock
            .Setup(c => c.RunAgentAsync(It.IsAny<AgentRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { Success = true });

        var svc = new SchedulerService(
            agentRunnerMock.Object,
            new CronExpressionEvaluator(NullLogger<CronExpressionEvaluator>.Instance),
            new HierarchicalMemoryStore(_tempDir, NullLogger<HierarchicalMemoryStore>.Instance),
            _sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AgentOptions()),
            Options.Create(new HoneybadgerOptions()),
            NullLogger<SchedulerService>.Instance);

        var tickMethod = typeof(SchedulerService).GetMethod("TickAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var before = DateTimeOffset.UtcNow;
        await (Task)tickMethod.Invoke(svc, [CancellationToken.None])!;

        using var verifyScope = _sp.CreateScope();
        var taskRepo = verifyScope.ServiceProvider.GetRequiredService<TaskRepository>();
        var task = await taskRepo.GetByIdAsync(1);

        task!.Status.Should().Be(TaskStatusData.Active, "interval task remains active");
        task.NextRunAt.Should().BeCloseTo(before.AddSeconds(intervalSeconds), TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
