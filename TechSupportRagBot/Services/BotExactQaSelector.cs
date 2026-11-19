using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class BotExactQaSelector
{
    public static RagChunkCandidate? Select(string question, IReadOnlyList<RagChunkCandidate> chunks)
    {
        var normalizedQuestion = Normalize(question);
        var faultIndex = normalizedQuestion.LastIndexOf("ошибка ", StringComparison.Ordinal);
        if (faultIndex > 0) normalizedQuestion = normalizedQuestion[faultIndex..];
        if (normalizedQuestion.Length < 12) return null;

        var ordered = chunks.OrderByDescending(x => x.RerankScore).ToList();
        if (ordered.Count == 0) return null;
        var best = ordered[0];
        if (!string.Equals(best.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(best.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(best.Solution)
            || !Normalize(best.Title ?? string.Empty).StartsWith(normalizedQuestion, StringComparison.Ordinal)
            || (ordered.Count > 1 && best.RerankScore - ordered[1].RerankScore < 0.35))
            return null;
        return best;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
