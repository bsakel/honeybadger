namespace Honeybadger.Core.Models;

/// <summary>
/// Message format for named-pipe communication between service and chat clients.
/// Uses newline-delimited JSON (NDJSON) protocol.
/// </summary>
public record PipeMessage
{
    public required string Type { get; init; }
    public string? GroupName { get; init; }
    public string? Content { get; init; }
    public string? Sender { get; init; }
    public bool IsFromAgent { get; init; }
    public string? Chunk { get; init; }

    public static class Types
    {
        public const string Register = "register";
        public const string UserMessage = "user_message";
        public const string ThinkingShow = "thinking_show";
        public const string ThinkingHide = "thinking_hide";
        public const string StreamChunk = "stream_chunk";
        public const string StreamDone = "stream_done";
        public const string AgentMessage = "agent_message";
    }
}
