using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Selects a short literal excerpt for a follow-up when synthesis is not grounded.</summary>
public static partial class BotEvidenceExcerptSelector
{
    public static string? Select(string source, string question, string draftAnswer)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var questionTerms = Terms(question);
        var draftTerms = Terms(draftAnswer);
        var sentences = SentenceSplit().Split(source.Trim())
            .Select((text, index) => new { Text = text.Trim(), Index = index })
            .Where(item => item.Text.Length is >= 20 and <= 500)
            .Select(item => new
            {
                item.Text,
                item.Index,
                Score = Terms(item.Text).Sum(term =>
                    (questionTerms.Contains(term) ? 2 : 0) + (draftTerms.Contains(term) ? 1 : 0))
            })
            .Where(item => item.Score >= 2)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(2)
            .OrderBy(item => item.Index)
            .Select(item => item.Text)
            .ToArray();
        return sentences.Length == 0 ? null : string.Join(" ", sentences);
    }

    private static HashSet<string> Terms(string text) => Word().Matches(text.ToLowerInvariant())
        .Select(match => BotLexicalTerms.Stem(match.Value))
        .Where(term => term is not ("прове" or "датчи" or "стано" or "ошибк"))
        .ToHashSet();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{Lu}])")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"[\p{L}]{5,}")]
    private static partial Regex Word();
}
