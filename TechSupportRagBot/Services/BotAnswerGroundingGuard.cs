using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Rejects model-added measurements and procedures absent from the selected evidence.</summary>
public static partial class BotAnswerGroundingGuard
{
    public static bool HasUnsupportedClaims(string answer, string evidence)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(evidence)) return false;

        var evidenceNumbers = NumberWithUnit().Matches(evidence.ToLowerInvariant())
            .Select(match => NormalizeMeasurement(match.Value)).ToHashSet();
        if (NumberWithUnit().Matches(answer.ToLowerInvariant())
            .Any(match => !evidenceNumbers.Contains(NormalizeMeasurement(match.Value)))) return true;

        var evidenceTerms = Terms(evidence);
        foreach (var line in answer.Split('\n'))
        {
            if (!ProcedureVerb().IsMatch(line)) continue;
            var terms = Terms(line);
            if (terms.Count < 5) continue;
            var supported = terms.Count(evidenceTerms.Contains);
            if ((double)supported / terms.Count < 0.55) return true;
        }
        return false;
    }

    /// <summary>Rejects an answer that presents a source's scenario condition as a customer fact.</summary>
    public static bool AttributesUnreportedCondition(string answer, string? sourceTitle, string customerReport)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(sourceTitle)) return false;
        var reported = NormalizeWords(customerReport);
        foreach (Match match in ScenarioCondition().Matches(sourceTitle))
        {
            var condition = NormalizeWords(match.Value);
            if (reported.Contains(condition, StringComparison.Ordinal) || condition.Length < 7) continue;
            foreach (var sentence in Regex.Split(answer, @"(?<=[.!?])\s+|\n+"))
            {
                var normalizedSentence = NormalizeWords(sentence);
                if (!normalizedSentence.Contains(condition, StringComparison.Ordinal)) continue;
                if (!ConditionalWording().IsMatch(sentence)) return true;
            }
        }
        return false;
    }

    private static string NormalizeWords(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string NormalizeMeasurement(string text) =>
        Regex.Replace(text.ToLowerInvariant().Replace(',', '.'), @"\s+", "");

    private static HashSet<string> Terms(string text) => Word().Matches(text.ToLowerInvariant())
        .Select(match => BotLexicalTerms.Stem(match.Value)).ToHashSet();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?\s*(?:в|v|вольт|бар|атм|кг|г|мм|см|мкм|°c|%|hz|гц)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWithUnit();

    [GeneratedRegex(@"\b(?:проверь\w*|убед\w*|настро\w*|замен\w*|измер\w*|подключ\w*|отключ\w*|сним\w*|нажм\w*|осмотр\w*|требу\w*|check\w*|replac\w*|measur\w*|adjust\w*|inspect\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProcedureVerb();

    [GeneratedRegex(@"[\p{L}]{5,}")]
    private static partial Regex Word();

    [GeneratedRegex(@"\b(?:после|перед|через|при)\s+[\p{L}\p{N}]+", RegexOptions.IgnoreCase)]
    private static partial Regex ScenarioCondition();

    [GeneratedRegex(@"\b(?:если|при условии|в случае|if|when)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionalWording();
}
