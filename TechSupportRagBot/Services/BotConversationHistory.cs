namespace TechSupportRagBot.Services;

/// <summary>Reads chat history while preserving wrapped and multi-line message bodies.</summary>
public static class BotConversationHistory
{
    public static IReadOnlyList<BotHistoryTurn> Parse(string? history)
    {
        var turns = new List<BotHistoryTurn>();
        foreach (var rawLine in (history ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            var role = separator > 0 ? line[..separator] : string.Empty;
            if (role is "Бот" or "Клиент" or "Оператор")
            {
                turns.Add(new BotHistoryTurn(role, line[(separator + 2)..]));
            }
            else if (turns.Count > 0)
            {
                var last = turns[^1];
                turns[^1] = last with { Text = last.Text + " " + line };
            }
        }
        return turns;
    }
}

public sealed record BotHistoryTurn(string Role, string Text);
