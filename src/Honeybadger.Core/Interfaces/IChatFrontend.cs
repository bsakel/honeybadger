using System.Threading.Channels;
using Honeybadger.Core.Models;

namespace Honeybadger.Core.Interfaces;

public interface IChatFrontend
{
    /// <summary>Incoming messages from the user, consumed by MessageLoopService.</summary>
    ChannelReader<ChatMessage> IncomingMessages { get; }

    /// <summary>Send a message to the user (agent response or system notification).</summary>
    Task SendToUserAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>Show a progress indicator while the agent is thinking.</summary>
    Task ShowAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Hide the progress indicator.</summary>
    Task HideAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Stream a chunk of the agent's response as it arrives (before the full message is complete).</summary>
    Task SendStreamChunkAsync(string groupName, string chunk, CancellationToken cancellationToken = default);

    /// <summary>Signal that streaming is complete and the final message is ready.</summary>
    Task SendStreamCompleteAsync(string groupName, CancellationToken cancellationToken = default);
}
