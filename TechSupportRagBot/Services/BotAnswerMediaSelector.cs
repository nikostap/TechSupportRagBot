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
        question = Regex.Replace(question.Trim(), @"^(?:(?:а|и)\s+)?(?:теперь\s+|ещ[её]\s+)?(?:пишет|показывает|выда[её]т|ошибка)\s+",
            "", RegexOptions.IgnoreCase);
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
                TitleTerms = Tokens(chunk.Title ?? string.Empty).ToHashSet(),
                PhraseMatch = Normalize(chunk.Title ?? string.Empty).Contains(Normalize(question), StringComparison.Ordinal)
            })
            .Where(item => numbers.Length == 0 || numbers.All(number => item.TitleTerms.Contains(number)))
            .Select(item => new
            {
                item.Chunk,
                Score = terms.Count(item.TitleTerms.Contains) + (item.PhraseMatch ? 3 : 0)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Chunk.RerankScore)
            .Select(item => item.Chunk.QAEntryId)
            .FirstOrDefault();
    }

    public static IReadOnlyList<int> SelectQaEntryIds(string question, IReadOnlyList<RagChunkCandidate> chunks)
    {
        var segments = Regex.Split(question, @"\s+и\s+|\s+and\s+", RegexOptions.IgnoreCase);
        if (segments.Length is < 2 or > 3)
            return SelectQaEntryId(question, chunks) is int one ? [one] : [];
        if (segments.Any(segment => !Tokens(segment)
            .Any(term => !term.All(char.IsDigit) && !GenericTerms.Contains(term))))
            return SelectQaEntryId(question, chunks) is int fallback ? [fallback] : [];
        var ids = segments.Select(segment => SelectQaEntryId(segment, chunks))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        return ids.Count >= 2 ? ids : SelectQaEntryId(question, chunks) is int single ? [single] : [];
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static IEnumerable<string> Tokens(string text) => Token.Matches(text.ToLowerInvariant())
        .Select(match => match.Value.All(char.IsDigit) ? match.Value : match.Value[..4]);
}
