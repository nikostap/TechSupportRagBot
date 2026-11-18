using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class BotSensorFocus
{
    private static readonly Regex Mention = new(@"\b(?:датчик[а-я]*|sensor)\s*(\d{2,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Filter(string question, RagSearchResult result)
    {
        var match = Mention.Match(question);
        if (!match.Success) return;
        var number = match.Groups[1].Value;
        var numberPattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(number)}(?![\p{{L}}\p{{N}}])";
        var matching = result.Chunks.Where(chunk => Regex.IsMatch(
            string.Join(' ', chunk.Title, chunk.Text, chunk.Solution, chunk.SearchQuestions, chunk.Tags),
            numberPattern)).ToList();
        if (matching.Count > 0) result.Chunks = matching;
    }
}
