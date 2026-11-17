using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Recognizes a client-supplied fault code without treating ordinary short replies as codes.</summary>
public static partial class BotTechnicalCode
{
    private static readonly HashSet<string> FaultReportWords = new(StringComparer.OrdinalIgnoreCase)
    { "error", "fault", "alarm", "code", "on", "new", "drive", "inverter", "appeared", "shows", "showing", "displayed", "now", "a", "the" };

    public static string? Extract(string question, string? history)
    {
        var explicitCode = LabeledCode().Match(question);
        if (explicitCode.Success && !FaultReportWords.Contains(explicitCode.Groups[1].Value))
            return explicitCode.Groups[1].Value;

        // Fault reports often put ordinary words between "ошибка" and a code.
        // This also handles alphabetic codes such as OCN when there is only one
        // plausible Latin token in the report.
        if (FaultContext().IsMatch(question))
        {
            var codes = FaultCodeToken().Matches(question).Select(x => x.Value)
                .Where(x => !FaultReportWords.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (codes.Count == 1) return codes[0];
        }

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

    public static int CountConsecutivePriorMentions(string code, string? history) =>
        BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .Reverse()
            .TakeWhile(x => string.Equals(Extract(x, null), code, StringComparison.OrdinalIgnoreCase))
            .Count();

    public static RagChunkCandidate? FindSingleNearMatch(string code, IReadOnlyList<RagChunkCandidate> candidates)
    {
        var near = candidates.Where(x => !string.IsNullOrWhiteSpace(x.ErrorCode)
                && IsOneEditAway(code, x.ErrorCode.Trim()))
            .GroupBy(x => x.ErrorCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return near.Count == 1 ? near[0].First() : null;
    }

    private static bool IsOneEditAway(string entered, string documented)
    {
        if (Math.Abs(entered.Length - documented.Length) > 1
            || Math.Min(entered.Length, documented.Length) < 2) return false;
        if (entered.Length != documented.Length)
        {
            var shorter = entered.Length < documented.Length ? entered : documented;
            var longer = entered.Length < documented.Length ? documented : entered;
            var i = 0;
            var j = 0;
            var skipped = false;
            while (i < shorter.Length)
            {
                if (char.ToUpperInvariant(shorter[i]) == char.ToUpperInvariant(longer[j]))
                {
                    i++;
                    j++;
                }
                else if (!skipped)
                {
                    skipped = true;
                    j++;
                }
                else return false;
            }
            return true;
        }
        var differences = Enumerable.Range(0, entered.Length)
            .Where(i => char.ToUpperInvariant(entered[i]) != char.ToUpperInvariant(documented[i]))
            .ToArray();
        if (differences.Length == 1) return true;
        return differences.Length == 2 && differences[1] == differences[0] + 1
            && char.ToUpperInvariant(entered[differences[0]]) == char.ToUpperInvariant(documented[differences[1]])
            && char.ToUpperInvariant(entered[differences[1]]) == char.ToUpperInvariant(documented[differences[0]]);
    }

    [GeneratedRegex(@"\b(?:ошибк\w*|авари\w*|код\w*|error|fault|alarm)\s*(?:№|#|:|-)?\s*([A-Za-z][A-Za-z0-9._-]{1,9})\b", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{1,9}$")]
    private static partial Regex CodeToken();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[A-Za-z][A-Za-z0-9._-]{1,9}(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)]
    private static partial Regex FaultCodeToken();

    [GeneratedRegex(@"ошибк|авари|код ошиб|инвертор|преобразовател|сервопривод|fault|error|alarm|inverter|drive", RegexOptions.IgnoreCase)]
    private static partial Regex FaultContext();
}
