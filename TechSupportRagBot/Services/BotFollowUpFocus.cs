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
