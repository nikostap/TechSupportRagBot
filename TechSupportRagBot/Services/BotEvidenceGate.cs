using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Conservatively checks that a follow-up's new technical terms occur in the retrieved evidence.</summary>
public static partial class BotEvidenceGate
{
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "этот", "этого", "этому", "такой", "такого", "который", "которая", "которое",
        "можно", "нужно", "после", "тогда", "когда", "если", "сделать", "пожалуйста",
        "какой", "какое", "какая", "какие", "какую", "почему", "здесь", "теперь", "работает",
        "использовать", "установить", "уточните", "уточнить",
        "about", "after", "before", "which", "there", "these", "those", "would", "could", "please"
    };

    public static IReadOnlyList<string> MissingTerms(string followUp, IReadOnlyList<RagChunkCandidate> chunks)
    {
        var terms = Words().Matches(followUp.ToLowerInvariant())
            .Select(x => x.Value)
            .Where(x => x.Length >= 5 && !CommonWords.Contains(x))
            .Select(x => x[..4])
            .Distinct()
            .ToList();
        if (terms.Count == 0) return [];

        var evidence = string.Join(' ', chunks.Select(x => string.Concat(x.Title, ' ', x.Text, ' ', x.Solution)))
            .ToLowerInvariant();
        return terms.Where(x => !evidence.Contains(x, StringComparison.Ordinal)).ToList();
    }

    [GeneratedRegex(@"[\p{L}]{5,}")]
    private static partial Regex Words();
}
