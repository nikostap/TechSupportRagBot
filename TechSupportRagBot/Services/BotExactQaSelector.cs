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

        return chunks
            .Where(chunk => string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(chunk.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(chunk.Solution)
                && Normalize(chunk.Title ?? string.Empty).StartsWith(normalizedQuestion, StringComparison.Ordinal))
            .OrderByDescending(chunk => chunk.RerankScore)
            .FirstOrDefault();
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
