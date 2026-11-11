using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

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

        var conversationPlan = BotConversationPlanner.Create(question, conversationContext);
        if (conversationPlan.NeedsChoiceClarification)
        {
            return new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotChooseClarification"), false, []);
        }
        if (conversationPlan.NeedsDetailClarification)
        {
            return new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotDescribeFollowUp"), false, []);
        }
        var retrievalQuestion = await BuildRussianRetrievalQuestionAsync(conversationPlan.SearchQuestion, language, traceId, cancellationToken);
        var ragResult = await _ragSearch.SearchAsync(new RagSearchRequest
        {
            TraceId = traceId,
            Question = retrievalQuestion,
            MachineId = machineId,
            FinalTopK = 5
        }, cancellationToken);

        var referenceCode = conversationPlan.IsFollowUp
            ? Regex.Match(question, @"\b[A-Za-z]{1,3}\d{2,4}\b", RegexOptions.IgnoreCase).Value
            : string.Empty;
        if (conversationPlan.IsFollowUp && string.IsNullOrEmpty(referenceCode))
            referenceCode = Regex.Match(conversationPlan.SearchQuestion, @"\b[A-Za-z]{1,3}\d{2,4}\b", RegexOptions.IgnoreCase).Value;
        FilterByReferenceCode(ragResult, referenceCode);

        var evidenceQuestion = conversationPlan.IsFollowUp && isEnglish
            ? await _translation.TranslateToRussianForSearchAsync(question, language, cancellationToken)
            : question;
        var missingTerms = conversationPlan.IsFollowUp
            ? BotEvidenceGate.MissingTerms(evidenceQuestion, ragResult.Chunks)
            : [];
        string? supplementalRetrievalQuestion = null;
        if (conversationPlan.IsFollowUp && (ragResult.ShouldCallOperator || missingTerms.Count > 0))
        {
            supplementalRetrievalQuestion = await BuildRussianRetrievalQuestionAsync(
                conversationPlan.FocusedSearchQuestion, language, traceId, cancellationToken);
            var focusedResult = await _ragSearch.SearchAsync(new RagSearchRequest
            {
                TraceId = traceId,
                Question = supplementalRetrievalQuestion,
                MachineId = machineId,
                FinalTopK = 5
            }, cancellationToken);
            FilterByReferenceCode(focusedResult, referenceCode);
            var merged = focusedResult.Chunks.Concat(ragResult.Chunks)
                .DistinctBy(x => x.ChunkId).Take(8).ToList();
            ragResult.Chunks = merged;
            ragResult.ShouldCallOperator = merged.Count == 0
                || (ragResult.ShouldCallOperator && focusedResult.ShouldCallOperator);
            ragResult.Confidence = Math.Max(ragResult.Confidence, focusedResult.Confidence);
            missingTerms = BotEvidenceGate.MissingTerms(evidenceQuestion, merged);
        }

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

            return new BotAnswerResult(
                await BuildClarificationAnswerAsync(ragResult, language, isEnglish, cancellationToken),
                false,
                []) { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult };
        }

        if (conversationPlan.IsFollowUp && missingTerms.Count > 0)
        {
            return new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotFollowUpEvidenceMissing"), false, [])
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = _ragSearch.BuildContextForLlm(ragResult) };
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

        if (conversationPlan.IsFollowUp && ragResult.Chunks.Count == 1)
        {
            var exactQa = ragResult.Chunks[0];
            if (string.Equals(exactQa.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(exactQa.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(exactQa.Solution))
            {
                var directAnswer = isEnglish
                    ? await _translation.TranslateAsync(exactQa.Solution, "Russian", language, cancellationToken)
                    : exactQa.Solution;
                if (!string.IsNullOrWhiteSpace(directAnswer))
                {
                    directAnswer = BotResponseDeduplicator.RemoveRepeatedSteps(directAnswer.Trim(), conversationPlan.PreviousBotAnswer);
                    if (string.IsNullOrWhiteSpace(directAnswer))
                        directAnswer = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotNoNewSteps");
                    return new BotAnswerResult(directAnswer, false, answerMedia)
                    { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context };
                }
            }
        }

        var promptHistory = conversationPlan.IsFollowUp
            ? TrimForPrompt(conversationContext ?? string.Empty, 1100)
            : "История не используется: новый вопрос.";
        var historyIncluded = conversationPlan.IsFollowUp;

        var prompt = $"""
        Ты инженер высокой квалификации и специалист технической поддержки станков.

        ВАЖНО:
        - Отвечай строго на языке: {language}.
        - Единственный источник технических фактов: "Контекст базы знаний".
        - Справочная история обращения не является источником фактов.
        - Историю используй только для понимания коротких уточнений вроде "да", "нет", "это", "тот датчик".
        - Если текущий вопрос самостоятельный, игнорируй историю и отвечай только по текущему вопросу и контексту базы знаний.
        - Если история противоречит найденному контексту, игнорируй историю.
        - Не выдумывай факты, номера ошибок, причины и действия.
        - Если точной информации нет, напиши: "Информация не найдена в документации."
        - Для нового вопроса сначала дай фактическую инструкцию из контекста. Для продолжения дай только новое действие или ответ на уточнение.
        - Не добавляй источники, файлы, страницы и названия документов в текст ответа.
        - Не используй шаблонные заголовки: "Краткий вывод", "Порядок проверки", "Порядок устранения", "Источник".
        - Отвечай как живой инженер техподдержки: связно, спокойно, по делу.
        - Если нужны шаги, используй короткие абзацы или нумерованный список.
        - Не смешивай разные узлы станка.
        - Не перечисляй соседние функции и настройки, о которых клиент не спрашивал.
        - Если неясно, какой узел или какой именно симптом имеется в виду, задай один конкретный вопрос до перечисления разных процедур.
        - Если это продолжение диалога, отвечай именно на новое уточнение. Не повторяй уже предложенные шаги без новой причины и не пересказывай предыдущий ответ.
        - Если уточнение лишь называет узел или повторяет неисправность без нового признака, коротко подтверди понятое и спроси, что именно наблюдается; не выдавай прежнюю инструкцию повторно.
        - Не повторяй уже заданный уточняющий вопрос дословно. Если ответа всё ещё недостаточно, спроси об одном другом наблюдаемом признаке, который разделит возможные причины.
        - Если уточнение просит одно значение или параметр, дай это значение и необходимое условие в одном-двух предложениях.
        - Если клиент повторно спрашивает значение, которое уже прозвучало, всё равно назови точное значение из контекста базы знаний. Не заменяй его ссылкой на прежнюю инструкцию.
        - Если новый признак не позволяет дать дополнительный подтверждённый шаг, задай один конкретный уточняющий вопрос.

        Вопрос клиента:
        {question}

        Русский поисковый запрос, по которому был найден контекст:
        {retrievalQuestion}

        Контекст базы знаний:
        {context}

        Справочная история текущего обращения:
        {promptHistory}
        """;

        await _audit.WriteAsync("Bot.Prompt.Built", new
        {
            machineId,
            question,
            retrievalQuestion,
            language,
            historyIncluded,
            historyPolicy = historyIncluded
                ? "History is included only as reference for short follow-up clarification; knowledge context remains the only factual source."
                : "History is excluded because the current question looks standalone.",
            promptLength = prompt.Length,
            prompt
        }, traceId, cancellationToken);

        var answer = await _ollama.GenerateAsync(prompt, cancellationToken, ApiUsageCategories.BotAnswers);
        if (string.IsNullOrWhiteSpace(answer))
        {
            var deterministicAnswer = await BuildDeterministicAnswerFromContextAsync(ragResult, language, isEnglish, cancellationToken);
            await _audit.WriteAsync("Bot.Answer.Empty", new
            {
                machineId,
                question,
                deterministicFallbackUsed = !string.IsNullOrWhiteSpace(deterministicAnswer)
            }, traceId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(deterministicAnswer))
            {
                await _audit.WriteAsync("Bot.Answer.Completed", new
                {
                    machineId,
                    question,
                    retrievalQuestion,
                    language,
                    rawAnswer = (string?)null,
                    finalAnswer = deterministicAnswer,
                    mediaCount = answerMedia.Count,
                    shouldEscalate = false,
                    source = "deterministic-context-fallback"
                }, traceId, cancellationToken);

                return new BotAnswerResult(deterministicAnswer, false, answerMedia)
                { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context };
            }

            var fallback = isEnglish
                ? "I found context, but could not form a reliable answer. Please clarify the machine unit, error text, sensor number, or what happens on the screen. How else can I help? If you need an operator, write \"operator\" in the chat."
                : "Контекст найден, но я не смог сформировать надежный ответ. Уточните узел станка, текст ошибки, номер датчика или что именно происходит на экране. Чем могу помочь ещё? Если нужен оператор, напишите в чат: оператор.";
            return new BotAnswerResult(fallback, false, answerMedia)
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context };
        }

        var cleanAnswer = CleanHumanAnswer(answer.Trim());
        if (conversationPlan.IsFollowUp)
        {
            cleanAnswer = BotResponseDeduplicator.RemoveRepeatedSteps(cleanAnswer, conversationPlan.PreviousBotAnswer);
            if (cleanAnswer.Length < 120 && answer.Length > cleanAnswer.Length)
            {
                cleanAnswer = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotNoNewSteps");
            }
        }
        if (LooksLikeSourceOnlyAnswer(cleanAnswer))
        {
            cleanAnswer = await BuildClarificationAnswerAsync(ragResult, language, isEnglish, cancellationToken);
        }

        var mustEscalate = ShouldEscalateFromAnswer(cleanAnswer);
        if (!mustEscalate && !conversationPlan.IsFollowUp && !ContainsHelpFollowup(cleanAnswer))
        {
            cleanAnswer += "\n\n" + HelpFollowup(isEnglish);
        }

        if (!mustEscalate && !conversationPlan.IsFollowUp && !ContainsOperatorHint(cleanAnswer))
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

        return new BotAnswerResult(cleanAnswer, mustEscalate, answerMedia)
        { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context };
    }

    private async Task<string?> BuildDeterministicAnswerFromContextAsync(
        RagSearchResult ragResult,
        string language,
        bool isEnglish,
        CancellationToken cancellationToken)
    {
        var qa = ragResult.Chunks
            .Where(x => string.Equals(x.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Solution))
            .OrderByDescending(x => x.RerankScore)
            .FirstOrDefault();

        var answer = qa?.Solution?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        if (!language.Equals("Russian", StringComparison.OrdinalIgnoreCase))
        {
            var translated = await _translation.TranslateAsync(answer, "Russian", language, cancellationToken);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                answer = translated;
            }
        }

        if (!ContainsHelpFollowup(answer))
        {
            answer += "\n\n" + HelpFollowup(isEnglish);
        }

        if (!ContainsOperatorHint(answer))
        {
            answer += "\n\n" + OperatorHint(isEnglish);
        }

        return answer;
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
            .Where(x => qaIds.Contains(x.QAEntryId)
                && x.QAEntry != null
                && x.QAEntry.Status == QAEntryStatuses.Verified)
            .OrderBy(x => x.Id)
            .Select(x => new BotAnswerMedia(
                x.PublicId,
                x.OriginalFileName,
                x.StoredFileName,
                x.FilePath,
                x.StorageProvider,
                x.ContentType,
                x.SizeBytes))
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

    private static string TrimForPrompt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[^maxLength..];
    }

    private static void FilterByReferenceCode(RagSearchResult result, string referenceCode)
    {
        if (string.IsNullOrWhiteSpace(referenceCode)) return;
        result.Chunks = result.Chunks.Where(x =>
            string.Concat(x.Title, " ", x.Text, " ", x.Solution)
                .Contains(referenceCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (result.Chunks.Count == 0) result.ShouldCallOperator = true;
    }

    private async Task<string> BuildClarificationAnswerAsync(
        RagSearchResult ragResult,
        string language,
        bool isEnglish,
        CancellationToken cancellationToken)
    {
        var similar = ragResult.Chunks
            .Select(x => FirstNotBlank(x.ErrorName, x.Title, x.SectionTitle, x.NodeName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (isEnglish)
        {
            var translatedSimilar = new List<string>();
            foreach (var item in similar)
            {
                var translated = await _translation.TranslateAsync(item!, "Russian", language, cancellationToken);
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    translatedSimilar.Add(translated);
                }
                else if (!ContainsCyrillic(item!))
                {
                    translatedSimilar.Add(item!);
                }
            }

            var answer = "I did not find exact information in the documentation for this request, so I will not guess.";
            if (translatedSimilar.Count > 0)
            {
                answer += "\n\nI found something similar: " + string.Join("; ", translatedSimilar) + ". Do you mean one of these?";
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

    private static bool ContainsCyrillic(string value) => value.Any(ch => ch is >= '\u0400' and <= '\u04FF');

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
    public string? RetrievalQuestion { get; init; }
    public string? SupplementalRetrievalQuestion { get; init; }
    public RagSearchResult? SearchResult { get; init; }
    public string? ContextForLlm { get; init; }
    public static BotAnswerResult Escalate(string text) => new(text, true, []);
}

public sealed record BotAnswerMedia(
    Guid PublicId,
    string OriginalFileName,
    string StoredFileName,
    string FilePath,
    string StorageProvider,
    string ContentType,
    long SizeBytes);
