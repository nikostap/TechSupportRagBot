using System.Text.Json;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class ResolvedTicketKnowledgeService
{
    private readonly OllamaClient _ollama;
    private readonly RagAuditLogger _audit;

    public ResolvedTicketKnowledgeService(OllamaClient ollama, RagAuditLogger audit)
    {
        _ollama = ollama;
        _audit = audit;
    }

    public async Task<ResolvedTicketKnowledge> BuildAsync(
        Ticket ticket,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var usefulMessages = messages
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.CreatedAt)
            .Select(x =>
            {
                var role = x.IsBotMessage
                    ? "Бот"
                    : x.AuthorUserId == ticket.ClientUserId
                        ? "Клиент"
                        : "Оператор";

                return $"{role}: {Trim(x.Text, 1200)}";
            })
            .ToList();

        var transcript = string.Join("\n", usefulMessages.TakeLast(30));
        await _audit.WriteAsync("ResolvedTicket.Extract.Started", new
        {
            ticket.Id,
            ticket.Title,
            transcript
        }, traceId, cancellationToken);

        var prompt = $$"""
        Ты готовишь решенное обращение техподдержки для базы знаний RAG.

        Из переписки нужно выделить только полезную пару вопрос-ответ:
        - основной технический вопрос клиента;
        - фактическое решение, после которого проблема считается решенной;
        - короткую тему;
        - категорию;
        - теги.

        Убери мусор: приветствия, повторы, благодарности, неверные предположения, промежуточные ответы, которые не решили проблему.
        Если в переписке были ошибочные ответы, не включай их в итоговый ответ.
        Не добавляй общие знания, которых нет в переписке.

        Верни только JSON без пояснений:
        {
          "question": "...",
          "answer": "...",
          "topic": "...",
          "category": "...",
          "tags": ["...", "..."]
        }

        Тема обращения: {{ticket.Title}}

        Переписка:
        {{transcript}}
        """;

        var raw = await _ollama.GenerateAsync(prompt, cancellationToken);
        var extracted = TryParse(raw);
        if (extracted == null || string.IsNullOrWhiteSpace(extracted.Question) || string.IsNullOrWhiteSpace(extracted.Answer))
        {
            extracted = Fallback(ticket, messages);
        }

        await _audit.WriteAsync("ResolvedTicket.Extract.Completed", new
        {
            ticket.Id,
            raw,
            extracted.Question,
            extracted.Answer,
            extracted.Topic,
            extracted.Category,
            extracted.Tags
        }, traceId, cancellationToken);

        return extracted;
    }

    private static ResolvedTicketKnowledge? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var json = raw.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = json[start..(end + 1)];
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ResolvedTicketKnowledgeDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
            {
                return null;
            }

            return new ResolvedTicketKnowledge(
                dto.Question?.Trim() ?? string.Empty,
                dto.Answer?.Trim() ?? string.Empty,
                dto.Topic?.Trim(),
                dto.Category?.Trim(),
                dto.Tags?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList() ?? []);
        }
        catch
        {
            return null;
        }
    }

    private static ResolvedTicketKnowledge Fallback(Ticket ticket, IReadOnlyList<ChatMessage> messages)
    {
        var question = messages
            .Where(x => x.AuthorUserId == ticket.ClientUserId && !string.IsNullOrWhiteSpace(x.Text))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Text.Trim())
            .FirstOrDefault() ?? ticket.Title;

        var answer = messages
            .Where(x => x.AuthorUserId != ticket.ClientUserId && !x.IsBotMessage && !string.IsNullOrWhiteSpace(x.Text))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Text.Trim())
            .FirstOrDefault() ?? "Решение подтверждено оператором, но итоговый ответ не удалось выделить автоматически.";

        return new ResolvedTicketKnowledge(question, answer, ticket.Title, "Решённые обращения", []);
    }

    private static string Trim(string text, int maxLength)
    {
        var normalized = string.Join(" ", text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private sealed class ResolvedTicketKnowledgeDto
    {
        public string? Question { get; set; }

        public string? Answer { get; set; }

        public string? Topic { get; set; }

        public string? Category { get; set; }

        public List<string>? Tags { get; set; }
    }
}

public sealed record ResolvedTicketKnowledge(
    string Question,
    string Answer,
    string? Topic,
    string? Category,
    IReadOnlyList<string> Tags);
