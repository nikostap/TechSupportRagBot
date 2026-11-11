using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static partial class BotResponseDeduplicator
{
    public static string RemoveRepeatedSteps(string answer, string previousAnswer)
    {
        if (string.IsNullOrWhiteSpace(previousAnswer)) return answer;
        var oldSteps = StepSegment().Matches(previousAnswer).Select(x => Tokens(x.Value)).ToList();
        var oldSentences = Sentence().Matches(previousAnswer).Select(x => Tokens(x.Value)).Where(x => x.Count >= 7).ToList();
        var oldLines = previousAnswer.Split('\n').Select(Tokens).Where(x => x.Count >= 3).ToList();

        var lines = answer.Split('\n');
        var retained = new List<string>();
        var skipStepDetails = false;
        foreach (var line in lines)
        {
            // A repeated measurement can be the direct answer to a follow-up question.
            // Never discard a documented value merely because it appeared earlier.
            if (TechnicalValue().IsMatch(line))
            {
                skipStepDetails = false;
                retained.Add(line);
                continue;
            }
            if (IsStep(line))
            {
                skipStepDetails = oldSteps.Any(old => Similarity(Tokens(line), old) >= 0.48)
                    || oldLines.Any(old => ContainsSameAction(Tokens(line), old));
                if (skipStepDetails) continue;
            }
            else if (skipStepDetails && (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('-')))
            {
                continue;
            }
            else
            {
                skipStepDetails = false;
            }

            if (line.Trim().Length > 40 && (oldSentences.Any(old => Similarity(Tokens(line), old) >= 0.78)
                || oldLines.Any(old => ContainsSameAction(Tokens(line), old)))) continue;
            retained.Add(line);
        }
        if (retained.Count == lines.Length) return answer;
        var stepNumber = 0;
        var result = string.Join("\n", retained.Select(line => IsStep(line)
            ? StepPrefix().Replace(line, $"{++stepNumber}. ", 1)
            : line)).Trim();
        return result;
    }

    private static bool IsStep(string line) => StepPrefix().IsMatch(line);

    private static HashSet<string> Tokens(string line) => Word().Matches(line.ToLowerInvariant())
        .Select(x => x.Value).Where(x => x.Length >= 3)
        .Select(x => x.Length > 5 ? x[..5] : x).ToHashSet();

    private static bool ContainsSameAction(HashSet<string> current, HashSet<string> old)
    {
        if (current.Count < 3) return false;
        var common = current.Intersect(old).Count();
        return common >= 3 && (double)common / current.Count >= 0.75;
    }

    private static double Similarity(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        return (double)left.Intersect(right).Count() / left.Union(right).Count();
    }

    [GeneratedRegex(@"^\s*\d+[.)]\s+")]
    private static partial Regex StepPrefix();

    [GeneratedRegex(@"\b\d+[.)]\s+.*?[.!?](?=\s|$)", RegexOptions.Singleline)]
    private static partial Regex StepSegment();

    [GeneratedRegex(@"[^.!?]+[.!?]")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*(?:кг|г|мм|см|мкм|в|а|°|%|kg|mm|cm|µm|v|a)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalValue();
}
