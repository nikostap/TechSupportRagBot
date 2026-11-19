using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Keeps sources about explicitly named two-digit sensor or indicator numbers.</summary>
public static class BotNumericReferenceFilter
{
    public static IReadOnlyList<RagChunkCandidate> Select(string question, IReadOnlyList<RagChunkCandidate> chunks)
    {
        var numbers = Regex.Matches(question, @"(?<!\d)\d{2}(?!\d)")
            .Select(x => x.Value).Distinct().ToList();
        if (numbers.Count == 0) return chunks;
        var matched = chunks.Where(chunk => numbers.All(number => Regex.IsMatch(
            $"{chunk.Title} {chunk.SearchQuestions} {chunk.Tags} {chunk.Text}",
            $@"(?<!\d){Regex.Escape(number)}(?!\d)"))).ToList();
        return matched.Count > 0 ? matched : chunks;
    }
}
