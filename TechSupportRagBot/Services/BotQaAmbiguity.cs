using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static partial class BotQaAmbiguity
{
    public static (string First, string Second)? GetChoices(string question,
        IReadOnlyList<RagChunkCandidate> chunks, bool isFollowUp)
    {
        if (isFollowUp || Word().Matches(question).Count > 5 || Number().IsMatch(question)) return null;
        var qa = chunks.Where(chunk => chunk.DocumentType == "QA"
                && string.Equals(chunk.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(chunk.Title))
            .OrderByDescending(chunk => chunk.RerankScore).Take(2).ToArray();
        if (qa.Length < 2 || qa[0].RerankScore - qa[1].RerankScore > 0.05
            || string.Equals(qa[0].Title, qa[1].Title, StringComparison.OrdinalIgnoreCase)) return null;
        return (qa[0].Title!.TrimEnd(' ', '?'), qa[1].Title!.TrimEnd(' ', '?'));
    }

    [GeneratedRegex(@"[\p{L}]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\d")]
    private static partial Regex Number();
}
