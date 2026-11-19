using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static partial class BotQaTitleSpecificity
{
    public static double UnrequestedSensorPenalty(string question, RagChunkCandidate candidate)
    {
        if (candidate.DocumentType != "QA" || string.IsNullOrWhiteSpace(candidate.Title)) return 0;
        var titleIds = Id().Matches(candidate.Title).Select(x => x.Value).Distinct().ToArray();
        if (titleIds.Length < 2) return 0;
        var questionIds = Id().Matches(question).Select(x => x.Value).ToHashSet();
        return titleIds.All(id => !questionIds.Contains(id)) ? -0.25 : 0;
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d{2,3}(?![\p{L}\p{N}])")]
    private static partial Regex Id();
}
