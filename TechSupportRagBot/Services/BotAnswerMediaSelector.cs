using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class BotAnswerMediaSelector
{
    private static readonly Regex Token = new(@"[\p{L}]{4,}|(?<![\p{L}\p{N}])\d{2,4}(?![\p{L}\p{N}])",
        RegexOptions.Compiled);
    private static readonly HashSet<string> GenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ошиб", "авар", "датч", "стан", "faul", "erro", "sens"
    };

    public static int? SelectQaEntryId(string question, IReadOnlyList<RagChunkCandidate> chunks)
    {
        var terms = Tokens(question).Where(term => !GenericTerms.Contains(term)).Distinct().ToArray();
        if (terms.Length == 0) return null;
        var numbers = terms.Where(term => term.All(char.IsDigit)).ToArray();
        return chunks
            .Where(chunk => chunk.DocumentType == "QA"
                && string.Equals(chunk.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && chunk.QAEntryId.HasValue)
            .Select(chunk => new
            {
                Chunk = chunk,
                TitleTerms = Tokens(chunk.Title ?? string.Empty).ToHashSet()
            })
            .Where(item => numbers.Length == 0 || numbers.All(number => item.TitleTerms.Contains(number)))
            .Select(item => new
            {
                item.Chunk,
                Score = terms.Count(item.TitleTerms.Contains)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Chunk.RerankScore)
            .Select(item => item.Chunk.QAEntryId)
            .FirstOrDefault();
    }

    private static IEnumerable<string> Tokens(string text) => Token.Matches(text.ToLowerInvariant())
        .Select(match => match.Value.All(char.IsDigit) ? match.Value : match.Value[..4]);
}
