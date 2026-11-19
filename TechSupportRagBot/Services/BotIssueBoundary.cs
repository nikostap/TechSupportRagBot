using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class BotIssueBoundary
{
    private static readonly Regex AdditionalFault = new(
        @"^\s*(?:(?:а|и)\s+)?(?:теперь|ещ[её]|также|сейчас|now|also)\b.{0,100}\b(?:ошиб\w*|авари\w*|пишет|показывает|выда[её]т|горит|снят\w*|не\s+работает|не\s+видит|fault|alarm|error|shows)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FaultReport = new(
        @"(?:^|(?<![\p{L}\p{N}]))(?:ошибка|авария|fault|alarm)\s+(.+?)\s*[.!?]*\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"(?<![\p{L}\p{N}])\d{2,4}(?![\p{L}\p{N}])", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[\p{L}]{4,}", RegexOptions.Compiled);
    private static readonly Regex TopicShift = new(
        @"^\s*(?:(?:а|и)\s+)?(?:теперь|ещ[её]|также|сейчас|now|also)\b[\s,:-]+(?<topic>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool StartsDifferentFault(string question, string? history)
    {
        if (AdditionalFault.IsMatch(question)
            && BotConversationHistory.Parse(history).Any(turn => turn.Role == "Клиент"))
        {
            var currentIssueTerms = IssueTerms(question);
            var previousIssueTerms = BotConversationHistory.Parse(history)
                .Where(turn => turn.Role == "Клиент")
                .Select(turn => IssueTerms(turn.Text))
                .LastOrDefault(terms => terms.Count > 0);
            if (previousIssueTerms is null || !currentIssueTerms.Overlaps(previousIssueTerms)) return true;
        }
        var shift = TopicShift.Match(question);
        if (shift.Success)
        {
            var currentTerms = IssueTerms(shift.Groups["topic"].Value);
            var previousTerms = BotConversationHistory.Parse(history)
                .Where(turn => turn.Role == "Клиент")
                .Select(turn => IssueTerms(turn.Text))
                .LastOrDefault(terms => terms.Count >= 1);
            if (currentTerms.Count >= 2 && previousTerms is { Count: >= 1 }
                && !currentTerms.Overlaps(previousTerms)) return true;
        }
        var current = Topic(question);
        if (current == null) return false;
        var previous = BotConversationHistory.Parse(history)
            .Where(turn => turn.Role == "Клиент")
            .Select(turn => Topic(turn.Text))
            .LastOrDefault(topic => topic != null);
        if (previous == null) return false;

        var currentNumbers = Number.Matches(current).Select(match => match.Value).ToHashSet();
        var previousNumbers = Number.Matches(previous).Select(match => match.Value).ToHashSet();
        if (currentNumbers.Count > 0 && previousNumbers.Count > 0
            && !currentNumbers.Overlaps(previousNumbers)) return true;

        var currentWords = Word.Matches(current).Select(match => match.Value[..4].ToLowerInvariant()).ToHashSet();
        var previousWords = Word.Matches(previous).Select(match => match.Value[..4].ToLowerInvariant()).ToHashSet();
        if (currentWords.Count > 0 && previousWords.Count > 0)
            return !currentWords.Overlaps(previousWords);
        return currentWords.Count > 0 && previousNumbers.Count > 0;
    }

    private static string? Topic(string text)
    {
        var match = FaultReport.Match(text.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    private static HashSet<string> IssueTerms(string text) => Word.Matches(text.ToLowerInvariant())
        .Select(match => BotLexicalTerms.Stem(match.Value))
        .Where(term => term is not ("тепер" or "сейча" or "меня" or "ошиб" or "также" or "ещё" or "еще"))
        .ToHashSet();
}
