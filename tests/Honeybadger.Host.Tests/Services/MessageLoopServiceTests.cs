using System.Threading.Channels;
using FluentAssertions;
using Honeybadger.Agent.Tools.Core;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Honeybadger.Data;
using Honeybadger.Data.Repositories;
using Honeybadger.Host.Agents;
using Honeybadger.Host.Memory;
using Honeybadger.Host.Scheduling;
using Honeybadger.Host.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Honeybadger.Host.Tests.Services;

public class MessageLoopServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _sp;

    public MessageLoopServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hb-mls-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDbContext<HoneybadgerDbContext>(o =>
            o.UseInMemoryDatabase("mls-test-" + Guid.NewGuid()));
        services.AddScoped<MessageRepository>();
        services.AddScoped<SessionRepository>();
        _sp = services.BuildServiceProvider();
    }

    [Fact]
    public async Task MessageLoop_RoutesMessageToAgent_AndDeliversResponse()
    {
        // Arrange
        var frontendMock = new Mock<IChatFrontend>();
        var incoming = Channel.CreateUnbounded<ChatMessage>();
        frontendMock.Setup(f => f.IncomingMessages).Returns(incoming.Reader);

        var sentMessages = new List<ChatMessage>();
        frontendMock.Setup(f => f.SendToUserAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ChatMessage, CancellationToken>((m, _) => sentMessages.Add(m))
            .Returns(Task.CompletedTask);
        frontendMock.Setup(f => f.ShowAgentThinkingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        frontendMock.Setup(f => f.HideAgentThinkingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var agentRunnerMock = new Mock<IAgentRunner>();
        agentRunnerMock.Setup(c => c.RunAgentAsync(
                It.IsAny<AgentRequest>(),
                It.IsAny<Func<string, Task>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<Microsoft.Extensions.AI.AIFunction>>()))
            .ReturnsAsync(new AgentResponse { Success = true, Content = "Hello from agent", SessionId = "sess-1" });

        var opts = Options.Create(new HoneybadgerOptions
        {
            Agent = new AgentOptions { DefaultModel = "test-model" }
        });

        using var queue = new GroupQueue(maxConcurrent: 1, NullLogger<GroupQueue>.Instance);
        var memoryStore = new HierarchicalMemoryStore(_tempDir, NullLogger<HierarchicalMemoryStore>.Instance);

        // Multi-agent infrastructure (empty registry for legacy mode)
        var agentRegistry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        // Don't load any configs, so GetRouterAgent() will return null (legacy mode)

        var ipcDir = Path.Combine(_tempDir, "ipc");
        Directory.CreateDirectory(ipcDir);
        var agentToolFactory = new AgentToolFactory(
            [new CoreToolProvider(ipcDir, NullLoggerFactory.Instance)]);

        using var cts = new CancellationTokenSource();
        var svc = new MessageLoopService(
            frontendMock.Object,
            agentRunnerMock.Object,
            queue,
            memoryStore,
            _sp.GetRequiredService<IServiceScopeFactory>(),
            opts,
            agentRegistry,
            agentToolFactory,
            NullLogger<MessageLoopService>.Instance);

        // Act
        var runTask = svc.StartAsync(cts.Token);

        await incoming.Writer.WriteAsync(new ChatMessage
        {
            GroupName = "main",
            Content = "Hello",
            Sender = "user"
        });

        // Wait for processing (generous timeout for CI/slow environments)
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (sentMessages.Count == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50);

        await cts.CancelAsync();

        // Assert
        agentRunnerMock.Verify(c => c.RunAgentAsync(
            It.Is<AgentRequest>(r => r.GroupName == "main" && r.Content == "Hello"),
            It.IsAny<Func<string, Task>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IEnumerable<Microsoft.Extensions.AI.AIFunction>>()), Times.Once);

        sentMessages.Should().HaveCount(1);
        sentMessages[0].Content.Should().Be("Hello from agent");
        sentMessages[0].IsFromAgent.Should().BeTrue();

        await runTask;
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
