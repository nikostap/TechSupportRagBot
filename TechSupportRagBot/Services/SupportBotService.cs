using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;

namespace TechSupportRagBot.Services;

public class SupportBotService
{
    private static readonly string[] OperatorTriggers =
    {
        "позови оператора",
        "позвать оператора",
        "оператор",
        "позови человека",
        "позвать человека",
        "нужен человек",
        "живой оператор",
        "свяжи с оператором",
        "соедини с оператором",
        "operator",
        "human",
        "call operator",
        "need operator",
        "need a human",
        "support agent"
    };

    private readonly OllamaClient _ollama;
    private readonly IRagSearchService _ragSearch;
    private readonly ILogger<SupportBotService> _logger;
    private readonly RagAuditLogger _audit;
    private readonly ApplicationDbContext _db;
    private readonly ChatTranslationService _translation;

    public SupportBotService(
        OllamaClient ollama,
        IRagSearchService ragSearch,
        ILogger<SupportBotService> logger,
        RagAuditLogger audit,
        ApplicationDbContext db,
        ChatTranslationService translation)
    {
        _ollama = ollama;
        _ragSearch = ragSearch;
        _logger = logger;
        _audit = audit;
        _db = db;
        _translation = translation;
    }

    public async Task<BotAnswerResult> AnswerAsync(
        string question,
        int machineId,
        string? userCountry = null,
        string? conversationContext = null,
        CancellationToken cancellationToken = default)
    {
        question = TextEncodingRepairService.RepairIfNeeded(question).Trim();
        conversationContext = TextEncodingRepairService.RepairIfNeeded(conversationContext);
        var language = ChatTranslationService.NormalizeLanguage(userCountry);
        var isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);
        var traceId = Guid.NewGuid().ToString("N");

        await _audit.WriteAsync("Bot.Request.Started", new
        {
            machineId,
            userLanguage = userCountry,
            language,
            question,
            conversationContext
        }, traceId, cancellationToken);

        if (ShouldCallOperator(question))
        {
            await _audit.WriteAsync("Bot.Request.EscalatedByUserTrigger", new { machineId, question }, traceId, cancellationToken);
            return BotAnswerResult.Escalate(EscalationText(isEnglish));
        }

        if (IsConfirmationReply(question) && !string.IsNullOrWhiteSpace(conversationContext))
        {
            return await AnswerFollowUpConfirmationAsync(question, machineId, language, isEnglish, conversationContext, traceId, cancellationToken);
        }

        var retrievalQuestion = await BuildRussianRetrievalQuestionAsync(question, language, traceId, cancellationToken);
        var ragResult = await _ragSearch.SearchAsync(new RagSearchRequest
        {
            TraceId = traceId,
            Question = retrievalQuestion,
            MachineId = machineId,
            FinalTopK = 8
        }, cancellationToken);

        _logger.LogInformation(
            "Bot RAG result. MachineId={MachineId}, Chunks={Chunks}, Confidence={Confidence:F3}, ShouldCallOperator={ShouldCallOperator}, Warning={Warning}",
            machineId,
            ragResult.Chunks.Count,
            ragResult.Confidence,
            ragResult.ShouldCallOperator,
            ragResult.Warning);

        if (ragResult.ShouldCallOperator || ragResult.Chunks.Count == 0)
        {
            await _audit.WriteAsync("Bot.Request.NeedsClarification", new
            {
                machineId,
                originalQuestion = question,
                retrievalQuestion,
                ragResult.Warning,
                ragResult.Confidence,
                chunksCount = ragResult.Chunks.Count
            }, traceId, cancellationToken);

            return new BotAnswerResult(BuildClarificationAnswer(ragResult, isEnglish), false, []);
        }

        var answerMedia = await LoadAnswerMediaAsync(ragResult, cancellationToken);
        var context = _ragSearch.BuildContextForLlm(ragResult);
        await _audit.WriteAsync("Rag.Context.Built", new
        {
            machineId,
            originalQuestion = question,
            retrievalQuestion,
            contextLength = context.Length,
            mediaCount = answerMedia.Count,
            context
        }, traceId, cancellationToken);

        var prompt = $"""
        Ты инженер высокой квалификации и специалист технической поддержки станков.

        ВАЖНО:
        - Отвечай строго на языке: {language}.
        - Используй только контекст ниже.
        - Не выдумывай факты, номера ошибок, причины и действия.
        - Если точной информации нет, напиши: "Информация не найдена в документации."
        - Сначала дай пользователю фактическую инструкцию из контекста.
        - Не добавляй источники, файлы, страницы и названия документов в текст ответа.
        - Не используй шаблонные заголовки: "Краткий вывод", "Порядок проверки", "Порядок устранения", "Источник".
        - Отвечай как живой инженер техподдержки: связно, спокойно, по делу.
        - Если нужны шаги, используй короткие абзацы или нумерованный список.
        - Не смешивай разные узлы станка.

        Контекст базы знаний:
        {context}

        Краткая история текущего обращения:
        {(string.IsNullOrWhiteSpace(conversationContext) ? "Истории нет." : conversationContext)}

        Вопрос клиента:
        {question}

        Русский поисковый запрос, по которому был найден контекст:
        {retrievalQuestion}
        """;

        await _audit.WriteAsync("Bot.Prompt.Built", new
        {
            machineId,
            question,
            retrievalQuestion,
            language,
            promptLength = prompt.Length,
            prompt
        }, traceId, cancellationToken);

        var answer = await _ollama.GenerateAsync(prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer))
        {
            await _audit.WriteAsync("Bot.Answer.Empty", new { machineId, question }, traceId, cancellationToken);
            var fallback = isEnglish
                ? "I could not form a reliable answer from the found context. Please clarify the machine unit, error text, sensor number, or what happens on the screen. How else can I help? If you need an operator, write \"operator\" in the chat."
                : "Я не смог сформировать надежный ответ по найденному контексту. Уточните узел станка, текст ошибки, номер датчика или что именно происходит на экране. Чем могу помочь ещё? Если нужен оператор, напишите в чат: оператор.";
            return new BotAnswerResult(fallback, false, []);
        }

        var cleanAnswer = CleanHumanAnswer(answer.Trim());
        if (LooksLikeSourceOnlyAnswer(cleanAnswer))
        {
            cleanAnswer = BuildClarificationAnswer(ragResult, isEnglish);
        }

        var mustEscalate = ShouldEscalateFromAnswer(cleanAnswer);
        if (!mustEscalate && !ContainsHelpFollowup(cleanAnswer))
        {
            cleanAnswer += "\n\n" + HelpFollowup(isEnglish);
        }

        if (!mustEscalate && !ContainsOperatorHint(cleanAnswer))
        {
            cleanAnswer += "\n\n" + OperatorHint(isEnglish);
        }

        await _audit.WriteAsync("Bot.Answer.Completed", new
        {
            machineId,
            question,
            retrievalQuestion,
            language,
            rawAnswer = answer,
            finalAnswer = cleanAnswer,
            mediaCount = answerMedia.Count,
            shouldEscalate = mustEscalate
        }, traceId, cancellationToken);

        return new BotAnswerResult(cleanAnswer, mustEscalate, answerMedia);
    }

    public static bool ShouldCallOperator(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return OperatorTriggers.Any(trigger => text.Contains(trigger, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<BotAnswerMedia>> LoadAnswerMediaAsync(RagSearchResult ragResult, CancellationToken cancellationToken)
    {
        var qaIds = ragResult.Chunks
            .Where(x => string.Equals(x.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && x.QAEntryId.HasValue)
            .OrderByDescending(x => x.RerankScore)
            .Select(x => x.QAEntryId!.Value)
            .Distinct()
            .Take(2)
            .ToArray();

        if (qaIds.Length == 0)
        {
            return [];
        }

        return await _db.QAAttachments
            .AsNoTracking()
            .Where(x => qaIds.Contains(x.QAEntryId))
            .OrderBy(x => x.Id)
            .Select(x => new BotAnswerMedia(x.OriginalFileName, x.StoredFileName, x.FilePath, x.ContentType, x.SizeBytes))
            .ToListAsync(cancellationToken);
    }

    private async Task<string> BuildRussianRetrievalQuestionAsync(string question, string language, string traceId, CancellationToken cancellationToken)
    {
        if (language.Equals("Russian", StringComparison.OrdinalIgnoreCase))
        {
            return question;
        }

        var retrievalQuestion = await _translation.TranslateToRussianForSearchAsync(question, language, cancellationToken);
        await _audit.WriteAsync("Bot.RetrievalQuestion.Built", new
        {
            originalQuestion = question,
            language,
            retrievalQuestion,
            translated = !string.Equals(retrievalQuestion, question, StringComparison.Ordinal)
        }, traceId, cancellationToken);
        return retrievalQuestion;
    }

    private async Task<BotAnswerResult> AnswerFollowUpConfirmationAsync(
        string question,
        int machineId,
        string language,
        bool isEnglish,
        string conversationContext,
        string traceId,
        CancellationToken cancellationToken)
    {
        var prompt = $"""
        Ты инженер техподдержки. Пользователь ответил коротко: "{question}".

        История диалога:
        {conversationContext}

        Правила:
        - Отвечай строго на языке: {language}.
        - Если пользователь подтвердил предположение бота, продолжи по этому предположению и попроси один-два конкретных уточняющих признака или предложи следующий безопасный шаг из уже названного контекста.
        - Если пользователь ответил отрицательно, попроси уточнить узел, ошибку, датчик, серийный номер или симптомы.
        - Не переводи к оператору автоматически.
        - В конце добавь, что если нужен оператор, можно написать "оператор".
        """;

        var answer = await _ollama.GenerateAsync(prompt, cancellationToken);
        var cleanAnswer = string.IsNullOrWhiteSpace(answer)
            ? (isEnglish
                ? "Please clarify the unit, error text, sensor number, or visible symptom. If you need an operator, write \"operator\" in the chat."
                : "Уточните, что именно происходит на станке: узел, текст ошибки, номер датчика или видимый симптом. Если нужен оператор, напишите в чат: оператор.")
            : CleanHumanAnswer(answer.Trim());

        if (!ContainsHelpFollowup(cleanAnswer))
        {
            cleanAnswer += "\n\n" + HelpFollowup(isEnglish);
        }

        if (!ContainsOperatorHint(cleanAnswer))
        {
            cleanAnswer += "\n\n" + OperatorHint(isEnglish);
        }

        await _audit.WriteAsync("Bot.Answer.FollowUpConfirmation", new
        {
            machineId,
            question,
            conversationContext,
            finalAnswer = cleanAnswer
        }, traceId, cancellationToken);

        return new BotAnswerResult(cleanAnswer, false, []);
    }

    private static string BuildClarificationAnswer(RagSearchResult ragResult, bool isEnglish)
    {
        var similar = ragResult.Chunks
            .Select(x => FirstNotBlank(x.ErrorName, x.Title, x.SectionTitle, x.NodeName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (isEnglish)
        {
            var answer = "I did not find exact information in the documentation for this request, so I will not guess.";
            if (similar.Count > 0)
            {
                answer += "\n\nI found something similar: " + string.Join("; ", similar) + ". Do you mean one of these?";
            }
            answer += "\n\nPlease clarify the unit, error text, sensor number, serial number, or what exactly happens on the screen.";
            answer += "\n\nHow else can I help?";
            answer += "\n\nIf you need an operator, write \"operator\" in the chat.";
            return answer;
        }

        var russian = "Точной информации в документации по этому запросу я не нашёл, поэтому не буду придумывать ответ.";
        if (similar.Count > 0)
        {
            russian += "\n\nНашёл похожее: " + string.Join("; ", similar) + ". Возможно, вы говорите про это?";
        }
        russian += "\n\nУточните узел станка, текст ошибки, номер датчика, серийный номер или что именно происходит на экране.";
        russian += "\n\nЧем могу помочь ещё?";
        russian += "\n\nЕсли нужен оператор, напишите в чат: оператор.";
        return russian;
    }

    private static string? FirstNotBlank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string CleanHumanAnswer(string answer)
    {
        return answer
            .Replace("Краткий вывод:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Порядок проверки:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Порядок устранения:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Источник:", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool ShouldEscalateFromAnswer(string answer)
    {
        return answer.Contains("передаю обращение оператору", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("forwarding the request", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfirmationReply(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        return normalized is "да" or "нет" or "ага" or "угу" or "верно" or "правильно" or "не то" or "не это" or "yes" or "no" or "yeah" or "yep" or "correct";
    }

    private static string EscalationText(bool isEnglish)
    {
        return isEnglish
            ? "I am forwarding the request to an operator. A specialist will respond during support hours: 8:00 AM to 5:00 PM Moscow time."
            : "Передаю обращение оператору. Специалист подключится в рабочее время техподдержки: с 8:00 до 17:00 по Москве.";
    }

    private static string HelpFollowup(bool isEnglish) => isEnglish ? "How else can I help?" : "Чем могу помочь ещё?";

    private static string OperatorHint(bool isEnglish) => isEnglish ? "If you need an operator, write \"operator\" in the chat." : "Если нужен оператор, напишите в чат: оператор.";

    private static bool ContainsHelpFollowup(string answer)
    {
        return answer.Contains("чем могу помочь", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("how else can i help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOperatorHint(string answer)
    {
        return answer.Contains("напишите в чат: оператор", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("write \"operator\"", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("write operator", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSourceOnlyAnswer(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 700)
        {
            return false;
        }

        var hasSourceReference = answer.Contains("раздел", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("документ", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("инструкц", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("section", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("document", StringComparison.OrdinalIgnoreCase);

        return hasSourceReference
            && (answer.Contains("проверьте раздел", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("смотрите раздел", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("refer to", StringComparison.OrdinalIgnoreCase)
                || answer.Contains("see section", StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

public sealed record BotAnswerResult(string Text, bool ShouldEscalate, IReadOnlyList<BotAnswerMedia> Media)
{
    public static BotAnswerResult Escalate(string text) => new(text, true, []);
}

public sealed record BotAnswerMedia(
    string OriginalFileName,
    string StoredFileName,
    string FilePath,
    string ContentType,
    long SizeBytes);
