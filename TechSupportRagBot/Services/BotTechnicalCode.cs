using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Recognizes a client-supplied fault code without treating ordinary short replies as codes.</summary>
public static partial class BotTechnicalCode
{
    private static readonly HashSet<string> FaultReportWords = new(StringComparer.OrdinalIgnoreCase)
    { "error", "fault", "alarm", "code", "on", "new", "drive", "inverter", "appeared", "shows", "showing", "displayed", "now", "a", "the" };

    public static string? Extract(string question, string? history, Machine? machine = null)
    {
        var explicitCode = LabeledCode().Match(question);
        if (explicitCode.Success && !FaultReportWords.Contains(explicitCode.Groups[1].Value)
            && !IsMachineDesignation(explicitCode.Groups[1].Value, question, machine))
            return explicitCode.Groups[1].Value;

        var displayedCode = DisplayedCode().Match(question);
        if (displayedCode.Success && !FaultReportWords.Contains(displayedCode.Groups[1].Value)
            && !IsMachineDesignation(displayedCode.Groups[1].Value, question, machine))
            return displayedCode.Groups[1].Value;

        // Fault reports often put ordinary words between "ошибка" and a code.
        // This also handles alphabetic codes such as OCN when there is only one
        // plausible Latin token in the report.
        if (FaultContext().IsMatch(question))
        {
            var codes = FaultCodeToken().Matches(question).Select(x => x.Value)
                .Where(x => !FaultReportWords.Contains(x) && !IsMachineDesignation(x, question, machine))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (codes.Count == 1) return codes[0];
        }

        var token = question.Trim().Trim('.', ',', ':', ';', '!', '?');
        if (!CodeToken().IsMatch(token) || IsMachineDesignation(token, question, machine)) return null;

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

    public static string? ExtractForFollowUp(string question, string? history, bool isFollowUp,
        Machine? machine = null)
    {
        var current = Extract(question, history, machine);
        if (current != null || !isFollowUp) return current;

        var clientTurns = BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .TakeLast(4)
            .Reverse();
        return clientTurns.Select(turn => Extract(turn, history, machine)).FirstOrDefault(code => code != null);
    }

    public static BotSuggestedCodeReply? ResolveSuggestedCodeReply(
        string question, string? history, BotTurnIntent interpretedIntent = BotTurnIntent.Unknown,
        Machine? machine = null)
    {
        var pending = GetPendingSuggestedCode(history);
        if (pending == null) return null;
        var reportedCode = pending.ReportedCode;
        var suggestedCode = pending.SuggestedCode;

        static bool Mentions(string text, string code) => Regex.IsMatch(text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(code)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
        var mentionsReported = Mentions(question, reportedCode);
        var mentionsSuggested = Mentions(question, suggestedCode);
        if (mentionsReported != mentionsSuggested)
            return pending with { SelectedCode = mentionsSuggested ? suggestedCode : reportedCode };
        if (interpretedIntent == BotTurnIntent.Affirmation
            || (interpretedIntent == BotTurnIntent.Unknown && question.Length <= 80 && AffirmativeReply().IsMatch(question.Trim())))
            return pending with { SelectedCode = suggestedCode };
        if (interpretedIntent == BotTurnIntent.Negation
            || (interpretedIntent == BotTurnIntent.Unknown && question.Length <= 80 && NegativeReply().IsMatch(question.Trim())))
            return pending;
        return null;
    }

    public static BotSuggestedCodeReply? GetPendingSuggestedCode(string? history)
    {
        var turns = BotConversationHistory.Parse(history);
        if (turns.LastOrDefault()?.Role != "Бот") return null;
        var suggestion = SuggestedCode().Match(turns[^1].Text);
        if (!suggestion.Success) return null;
        var reportedCode = GetLastReportedCode(history);
        var suggestedCode = suggestion.Groups[1].Value;
        if (reportedCode == null || string.Equals(reportedCode, suggestedCode, StringComparison.OrdinalIgnoreCase))
            return null;
        return new BotSuggestedCodeReply(reportedCode, suggestedCode, null);
    }

    public static string? GetLastReportedCode(string? history, Machine? machine = null) =>
        BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => Extract(x.Text, null, machine))
            .LastOrDefault(x => x != null);

    public static int CountConsecutivePriorMentions(string code, string? history, Machine? machine = null) =>
        BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .Reverse()
            .TakeWhile(x => string.Equals(Extract(x, null, machine), code, StringComparison.OrdinalIgnoreCase))
            .Count();

    private static bool IsMachineDesignation(string token, string question, Machine? machine)
    {
        static string Normalize(string value) => Regex.Replace(value, @"[^\p{L}\p{N}]", "").ToUpperInvariant();
        var normalized = Normalize(token);
        if (machine != null && normalized.Length >= 3)
        {
            var serial = machine.SerialNumber.Trim();
            var aliases = new[] { machine.Model, machine.Name,
                $"{machine.Model}-{serial}", $"{machine.Name}-{serial}" };
            if (aliases.Any(alias => normalized == Normalize(alias))) return true;
        }
        return token.Contains('-') && token.Any(char.IsDigit)
            && Regex.IsMatch(question,
                $@"\b(?:на\s+(?:станк\w*\s+)?|станок\s+|машин\w*\s+|модел\w*\s+|machine\s+|model\s+){Regex.Escape(token)}\b",
                RegexOptions.IgnoreCase);
    }

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

    [GeneratedRegex(@"\b(?:пишет|показывает|высвечивает|shows|displays)\s*(?:код\s*)?([A-Za-z][A-Za-z0-9._-]{1,9})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayedCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{1,9}$")]
    private static partial Regex CodeToken();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[A-Za-z][A-Za-z0-9._-]{1,9}(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)]
    private static partial Regex FaultCodeToken();

    [GeneratedRegex(@"ошибк|авари|код ошиб|пишет|показывает|высвечивает|инвертор|преобразовател|сервопривод|fault|error|alarm|shows|displays|inverter|drive", RegexOptions.IgnoreCase)]
    private static partial Regex FaultContext();

    [GeneratedRegex(@"(?:похожий код|similar code,?)\s+([A-Za-z][A-Za-z0-9._-]{1,9})", RegexOptions.IgnoreCase)]
    private static partial Regex SuggestedCode();

    [GeneratedRegex(@"^(?:да|ага|угу|верно|правильно|yes|yeah|yep|correct)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AffirmativeReply();

    [GeneratedRegex(@"^(?:нет|не она|не то|no|nope|wrong)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegativeReply();
}

public sealed record BotSuggestedCodeReply(string ReportedCode, string SuggestedCode, string? SelectedCode);
