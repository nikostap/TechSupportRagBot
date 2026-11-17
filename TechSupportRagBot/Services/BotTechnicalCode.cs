using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Recognizes a client-supplied fault code without treating ordinary short replies as codes.</summary>
public static partial class BotTechnicalCode
{
    public static string? Extract(string question, string? history)
    {
        var explicitCode = LabeledCode().Match(question);
        if (explicitCode.Success) return explicitCode.Groups[1].Value;

        var token = question.Trim().Trim('.', ',', ':', ';', '!', '?');
        if (!CodeToken().IsMatch(token)) return null;

        var turns = BotConversationHistory.Parse(history);
        var lastBot = turns.LastOrDefault(x => x.Role == "Бот")?.Text;
        var lastClient = turns.LastOrDefault(x => x.Role == "Клиент")?.Text;
        var context = string.Concat(lastClient, " ", lastBot);
        if (FaultContext().IsMatch(context)) return token;
        // With no history, a code-like identifier can still be searched by itself.
        return string.IsNullOrWhiteSpace(history) && (token.Any(char.IsDigit) || token.Any(char.IsUpper))
            ? token : null;
    }

    public static bool Matches(RagChunkCandidate chunk, string code) =>
        string.Equals(chunk.ErrorCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(string.Concat(chunk.Title, " ", chunk.Text, " ", chunk.Solution),
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(code)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    public static string? ExtractForFollowUp(string question, string? history, bool isFollowUp)
    {
        var current = Extract(question, history);
        if (current != null || !isFollowUp) return current;

        var clientTurns = BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .TakeLast(4)
            .Reverse();
        return clientTurns.Select(turn => Extract(turn, history)).FirstOrDefault(code => code != null);
    }

    [GeneratedRegex(@"\b(?:ошибк\w*|авари\w*|код\w*|error|fault|alarm)\s*(?:№|#|:|-)?\s*([A-Za-z][A-Za-z0-9._-]{1,9})\b", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{1,9}$")]
    private static partial Regex CodeToken();

    [GeneratedRegex(@"ошибк|авари|код ошиб|инвертор|преобразовател|сервопривод|fault|error|alarm|inverter|drive", RegexOptions.IgnoreCase)]
    private static partial Regex FaultContext();
}
