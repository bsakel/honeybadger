using Honeybadger.Core.Models;
using FluentAssertions;

namespace Honeybadger.Core.Tests.Models;

public class ChatMessageTests
{
    [Fact]
    public void ChatMessage_DefaultId_IsNotEmpty()
    {
        var msg = new ChatMessage();
        msg.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ChatMessage_DefaultTimestamp_IsUtc()
    {
        var msg = new ChatMessage();
        msg.Timestamp.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ChatMessage_InitProperties_SetCorrectly()
    {
        var msg = new ChatMessage
        {
            GroupName = "main",
            Content = "Hello",
            Sender = "user",
            IsFromAgent = false
        };

        msg.GroupName.Should().Be("main");
        msg.Content.Should().Be("Hello");
        msg.Sender.Should().Be("user");
        msg.IsFromAgent.Should().BeFalse();
    }
}
