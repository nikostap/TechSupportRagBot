using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Matches a correction's remaining symptom to earlier client issues by semantic similarity.</summary>
public static class BotFollowUpQueryRewriter
{
    public static async Task<BotIssueMatch?> RewriteAsync(string remainingSymptom, string? history,
        OllamaClient model, CancellationToken cancellationToken)
    {
        var clients = BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .TakeLast(3)
            .ToArray();
        if (clients.Length == 0) return null;
        var options = clients.SelectMany(SplitIssues).Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(6).ToArray();
        if (options.Length == 0) return null;
        var symptomVector = await model.EmbedAsync(remainingSymptom, cancellationToken, ApiUsageCategories.RagSearch);
        if (symptomVector is null) return null;
        var scored = new List<(string Issue, double Score)>();
        foreach (var option in options)
        {
            var vector = await model.EmbedAsync(option, cancellationToken, ApiUsageCategories.RagSearch);
            if (vector is not null) scored.Add((option, Cosine(symptomVector, vector)));
        }
        return Select(scored);
    }

    public static BotIssueMatch? Select(IReadOnlyList<(string Issue, double Score)> scored)
    {
        var ranked = scored.Where(x => double.IsFinite(x.Score)).OrderByDescending(x => x.Score).ToList();
        if (ranked.Count == 0) return null;
        var best = ranked[0];
        var margin = ranked.Count > 1 ? best.Score - ranked[1].Score : best.Score;
        return best.Score >= 0.65 && margin >= 0.06 && best.Issue.Length is >= 5 and <= 160
            ? new BotIssueMatch(best.Issue, best.Score, margin) : null;
    }

    public static double Cosine(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        if (first.Count == 0 || first.Count != second.Count) return 0;
        double dot = 0, firstNorm = 0, secondNorm = 0;
        for (var i = 0; i < first.Count; i++)
        {
            dot += first[i] * second[i];
            firstNorm += first[i] * first[i];
            secondNorm += second[i] * second[i];
        }
        return firstNorm <= 0 || secondNorm <= 0 ? 0 : dot / Math.Sqrt(firstNorm * secondNorm);
    }

    private static IEnumerable<string> SplitIssues(string turn) =>
        Regex.Split(turn, @"\s+и\s+|\s+and\s+", RegexOptions.IgnoreCase)
            .Select(x => x.Trim(' ', ',', '.', ';'))
            .Where(x => x.Length >= 5 && !BotQuestionSpecificity.NeedsSymptom(x));
}

public sealed record BotIssueMatch(string Query, double Similarity, double Margin);
