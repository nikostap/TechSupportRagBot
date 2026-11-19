using System.Text.Json;
using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Recovers the remaining issue from client-only history when a correction uses different everyday words.</summary>
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
        var prompt = $$"""
            Сформулируй ОДИН короткий самостоятельный поисковый запрос о неисправности,
            которую клиент всё ещё подтверждает. Используй только сообщения клиента как факты.
            Слова клиента могут быть бытовыми: восстанови прежнее название предмета из его же сообщений.
            Не включай отвергнутую причину, даже если она была в предыдущем сообщении.
            Не добавляй номер датчика, код, причину или действие, которых клиент не называл.
            Если невозможно понять, что осталось неисправным, верни пустую строку.
            Верни только JSON: {"query":"..."}.
            Предыдущие сообщения клиента: {{JsonSerializer.Serialize(clients)}}
            Текущее исправление клиента: {{JsonSerializer.Serialize(question)}}
            """;
        var raw = await model.GenerateAsync(prompt, cancellationToken, ApiUsageCategories.BotAnswers);
        return Parse(raw, question, clients);
    }

    public static string? Parse(string? raw, string question, IReadOnlyList<string> priorClientTurns)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var json = JsonDocument.Parse(raw[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("query", out var value)) return null;
            var query = value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(query) || query.Length is < 5 or > 160
                || BotQuestionSpecificity.NeedsSymptom(query)) return null;
            var source = string.Join(' ', priorClientTurns.Append(question));
            var allowedNumbers = Regex.Matches(source, @"\d{2,4}").Select(x => x.Value).ToHashSet();
            if (Regex.Matches(query, @"\d{2,4}").Any(x => !allowedNumbers.Contains(x.Value))) return null;
            var sourceTerms = Regex.Matches(source.ToLowerInvariant(), @"[\p{L}]{5,}")
                .Select(x => BotLexicalTerms.Stem(x.Value)).ToHashSet();
            if (!Regex.Matches(query.ToLowerInvariant(), @"[\p{L}]{5,}")
                .Any(x => sourceTerms.Contains(BotLexicalTerms.Stem(x.Value)))) return null;
            return query;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
