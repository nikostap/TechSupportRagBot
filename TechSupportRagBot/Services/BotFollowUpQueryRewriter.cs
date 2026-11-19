using System.Text.Json;
using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Selects the remaining client-reported issue when a correction uses different everyday words.</summary>
public static class BotFollowUpQueryRewriter
{
    public static async Task<string?> RewriteAsync(string question, string? history,
        OllamaClient model, CancellationToken cancellationToken)
    {
        var clients = BotConversationHistory.Parse(history)
            .Where(x => x.Role == "Клиент")
            .Select(x => x.Text)
            .TakeLast(3)
            .ToArray();
        if (clients.Length == 0) return null;
        var options = clients.SelectMany(SplitIssues).TakeLast(6).ToArray();
        if (options.Length == 0) return null;
        var prompt = $$"""
            Выбери ОДНУ из ранее названных клиентом неисправностей, которая остаётся актуальной
            после его последнего исправления. Слова клиента могут быть бытовыми синонимами.
            Не выбирай причину, которую клиент отверг. Если ни одна прежняя неисправность
            не подходит или выбор неясен, верни -1. Не отвечай на технический вопрос.
            Верни только JSON: {"index":0}, где index — номер элемента массива с нуля.
            Ранее названные неисправности: {{JsonSerializer.Serialize(options)}}
            Исправление клиента: {{JsonSerializer.Serialize(question)}}
            """;
        var raw = await model.GenerateAsync(prompt, cancellationToken, ApiUsageCategories.BotAnswers);
        return Parse(raw, options);
    }

    public static string? Parse(string? raw, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var json = JsonDocument.Parse(raw[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("index", out var value)
                || !value.TryGetInt32(out var index) || index < 0 || index >= options.Count) return null;
            var query = options[index].Trim();
            return query.Length is >= 5 and <= 160 && !BotQuestionSpecificity.NeedsSymptom(query)
                ? query : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SplitIssues(string turn) =>
        Regex.Split(turn, @"\s+и\s+|\s+and\s+", RegexOptions.IgnoreCase)
            .Select(x => x.Trim(' ', ',', '.', ';'))
            .Where(x => x.Length >= 5 && !BotQuestionSpecificity.NeedsSymptom(x));
}
