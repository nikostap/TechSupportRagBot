using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Prevents a different part's instruction from answering an explicit part-number question.</summary>
public static class BotIdentifierEvidenceGate
{
    public static IReadOnlyList<RagChunkCandidate> Select(string question, Machine? machine,
        IReadOnlyList<RagChunkCandidate> chunks)
    {
        var withoutMachine = BotMachineMention.Remove(question, machine);
        var identifiers = Regex.Matches(withoutMachine,
                @"(?<![A-Za-z0-9])(?=[A-Za-z0-9-]*\d)[A-Za-z][A-Za-z0-9-]{2,}(?![A-Za-z0-9])")
            .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (identifiers.Count == 0) return chunks;
        return chunks.Where(chunk => identifiers.All(identifier => Regex.IsMatch(
            $"{chunk.Title} {chunk.SearchQuestions} {chunk.Tags} {chunk.ErrorCode} {chunk.Text}",
            $@"(?<![A-Za-z0-9]){Regex.Escape(identifier)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase))).ToList();
    }
}
