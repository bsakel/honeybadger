using Honeybadger.Data;
using Honeybadger.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Honeybadger.Host.Tests.Data;

public class MessageRepositoryTests
{
    private static HoneybadgerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HoneybadgerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HoneybadgerDbContext(options);
    }

    [Fact]
    public async Task GetOrCreateChat_CreatesNewChat_WhenNotExists()
    {
        await using var db = CreateContext();
        var repo = new MessageRepository(db);

        var chat = await repo.GetOrCreateChatAsync("main");

        chat.Should().NotBeNull();
        chat.GroupName.Should().Be("main");
        chat.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetOrCreateChat_ReturnsExisting_WhenExists()
    {
        await using var db = CreateContext();
        var repo = new MessageRepository(db);

        var chat1 = await repo.GetOrCreateChatAsync("main");
        var chat2 = await repo.GetOrCreateChatAsync("main");

        chat1.Id.Should().Be(chat2.Id);
    }

    [Fact]
    public async Task AddMessage_SavesAndReturns()
    {
        await using var db = CreateContext();
        var repo = new MessageRepository(db);

        var msg = await repo.AddMessageAsync("main", "ext-1", "user", "Hello!", false);

        msg.Id.Should().BeGreaterThan(0);
        msg.Content.Should().Be("Hello!");
        msg.Sender.Should().Be("user");
    }

    [Fact]
    public async Task GetRecentMessages_ReturnsInChronologicalOrder()
    {
        await using var db = CreateContext();
        var repo = new MessageRepository(db);

        await repo.AddMessageAsync("main", "1", "user", "First", false);
        await Task.Delay(10); // ensure different timestamps
        await repo.AddMessageAsync("main", "2", "agent", "Second", true);

        var messages = await repo.GetRecentMessagesAsync("main");

        messages.Should().HaveCount(2);
        messages[0].Content.Should().Be("First");
        messages[1].Content.Should().Be("Second");
    }
}
