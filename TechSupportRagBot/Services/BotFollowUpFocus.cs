using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Keeps a short follow-up on the issue and sources discussed in the prior answer.</summary>
public static partial class BotFollowUpFocus
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "ошиб", "авари", "стано", "делат", "нужно", "можно", "помоч", "вопро", "после",
        "прове", "если", "which", "error", "fault", "about", "would", "could", "check"
    };
    private static readonly HashSet<string> DetailCommon = new[]
    {
        "ошибка", "ошибки", "станок", "станка", "после", "когда", "проверьте",
        "работает", "ситуация", "какой", "какая", "какие", "сейчас", "теперь",
        "пишет", "показывает", "делать"
    }.Select(BotLexicalTerms.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RagChunkCandidate> Select(
        IReadOnlyList<RagChunkCandidate> chunks, string? originalProblem, string? previousAnswer)
    {
        var issueTerms = Terms(originalProblem);
        if (issueTerms.Count == 0) return chunks;

        var onIssue = chunks.Where(chunk => Terms(Content(chunk)).Overlaps(issueTerms)).ToList();
        if (onIssue.Count <= 1) return onIssue;

        var answerTerms = Terms(previousAnswer);
        if (answerTerms.Count == 0) return onIssue;
        var scored = onIssue.Select(chunk => new
        {
            Chunk = chunk,
            Overlap = Terms(Content(chunk)).Intersect(answerTerms).Count()
        }).ToList();
        var best = scored.Max(x => x.Overlap);
        if (best < 3) return onIssue;
        return scored.Where(x => x.Overlap >= Math.Max(3, best * 0.75))
            .Select(x => x.Chunk).ToList();
    }

    /// <summary>
    /// Narrows retrieved evidence only when a new detail discriminates one source on the
    /// already established issue. The previous bot wording and QA IDs are not search facts.
    /// </summary>
    public static RagChunkCandidate? SelectByLatestDetail(
        IReadOnlyList<RagChunkCandidate> chunks, string? originalProblem, string latestDetail)
    {
        if (chunks.Count < 2) return null;
        var issueTerms = DetailTerms(originalProblem);
        var detailTerms = DetailTerms(latestDetail);
        detailTerms.ExceptWith(issueTerms);
        if (issueTerms.Count == 0 || detailTerms.Count == 0) return null;

        var scored = chunks.Select(chunk => new
        {
            Chunk = chunk,
            OnIssue = DetailTerms(Content(chunk)).Overlaps(issueTerms),
            DetailScore = DetailTerms(chunk.Title).Intersect(detailTerms).Count() * 3
                + DetailTerms(chunk.SectionTitle).Intersect(detailTerms).Count() * 2
                + DetailTerms(chunk.SearchQuestions).Intersect(detailTerms).Count()
                + DetailTerms(chunk.Tags).Intersect(detailTerms).Count()
                + DetailTerms(chunk.NodeName).Intersect(detailTerms).Count()
        })
            .Where(item => item.OnIssue)
            .OrderByDescending(item => item.DetailScore)
            .ThenByDescending(item => item.Chunk.RerankScore)
            .ToArray();
        if (scored.Length == 0 || scored[0].DetailScore == 0
            || (scored.Length > 1 && scored[0].DetailScore == scored[1].DetailScore)) return null;
        return scored[0].Chunk;
    }

    private static HashSet<string> DetailTerms(string? value) => Words().Matches(value?.ToLowerInvariant() ?? string.Empty)
        .Select(match => BotLexicalTerms.Stem(match.Value))
        .Where(term => !Common.Contains(term) && !DetailCommon.Contains(term))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Terms(string? value) => Words().Matches(value?.ToLowerInvariant() ?? string.Empty)
        .Select(match => match.Value)
        .Where(word => word.Length >= 5)
        .Select(word => word[..5])
        .Where(word => !Common.Contains(word))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Content(RagChunkCandidate chunk) => string.Join(' ',
        chunk.Title, chunk.Text, chunk.Solution, chunk.NodeName,
        chunk.SectionTitle, chunk.DocumentName, chunk.ErrorCode);

    [GeneratedRegex(@"[\p{L}\p{N}]{5,}")]
    private static partial Regex Words();
}
