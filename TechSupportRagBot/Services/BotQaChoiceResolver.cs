using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Resolves a short answer to one of two QA scenarios explicitly offered by the bot.</summary>
public static partial class BotQaChoiceResolver
{
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "ошиб", "стано", "после", "когда", "появ", "ситуа", "вариа", "нить", "нити"
    };

    public static RagChunkCandidate? Select(string reply, string? previousBotAnswer,
        IReadOnlyList<RagChunkCandidate> chunks)
    {
        if (string.IsNullOrWhiteSpace(previousBotAnswer) || reply.Length > 100) return null;
        var offered = QuotedChoice().Matches(previousBotAnswer).Select(match => match.Groups["choice"].Value)
            .Where(value => value.Length >= 12).Take(2).ToArray();
        if (offered.Length != 2) return null;

        var candidates = offered.Select(title => chunks.FirstOrDefault(chunk =>
            string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
            && string.Equals(chunk.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(chunk.Solution)
            && Normalize(chunk.Title ?? string.Empty) == Normalize(title))).ToArray();
        if (candidates.Any(chunk => chunk is null) || candidates[0]!.QAEntryId == candidates[1]!.QAEntryId)
            return null;

        var normalizedReply = Normalize(reply);
        if (normalizedReply is "первая" or "первый" or "первое" or "first") return candidates[0];
        if (normalizedReply is "вторая" or "второй" or "второе" or "second") return candidates[1];

        var replyTerms = Terms(reply);
        var firstTerms = Terms(offered[0]);
        var secondTerms = Terms(offered[1]);
        var firstScore = replyTerms.Intersect(firstTerms.Except(secondTerms)).Count();
        var secondScore = replyTerms.Intersect(secondTerms.Except(firstTerms)).Count();
        if (firstScore == secondScore || Math.Max(firstScore, secondScore) == 0) return null;
        return firstScore > secondScore ? candidates[0] : candidates[1];
    }

    private static HashSet<string> Terms(string text) => Word().Matches(text.ToLowerInvariant())
        .Select(match => BotLexicalTerms.Stem(match.Value))
        .Where(term => !Generic.Contains(term))
        .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    [GeneratedRegex("[«“](?<choice>[^»”]+)[»”]")]
    private static partial Regex QuotedChoice();

    [GeneratedRegex(@"[\p{L}]{4,}")]
    private static partial Regex Word();
}
