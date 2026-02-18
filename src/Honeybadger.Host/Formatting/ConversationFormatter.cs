using Honeybadger.Data.Entities;

namespace Honeybadger.Host.Formatting;

/// <summary>
/// Formats conversation history from database entities into a readable string for agent context.
/// </summary>
public static class ConversationFormatter
{
    public static string Format(IReadOnlyList<MessageEntity> messages, int tokenBudget = 0)
    {
        var sb = new System.Text.StringBuilder();
        var estimatedTokens = 0;

        // Messages are ordered oldest-first; iterate in reverse (newest first) to prioritize recent
        foreach (var m in messages.Reverse())
        {
            var line = $"[{m.Sender}]: {m.Content}\n";
            var lineTokens = line.Length / 4; // Approximate: 4 chars per token

            if (tokenBudget > 0 && estimatedTokens + lineTokens > tokenBudget)
                break;

            estimatedTokens += lineTokens;
            sb.Insert(0, line); // Prepend to maintain chronological order
        }

        return sb.ToString();
    }
}
