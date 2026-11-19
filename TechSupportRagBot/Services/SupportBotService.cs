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
        CancellationToken cancellationToken = default,
        Action<BotDecisionStep>? onDecisionStep = null)
    {
        question = TextEncodingRepairService.RepairIfNeeded(question).Trim();
        conversationContext = TextEncodingRepairService.RepairIfNeeded(conversationContext);
        var language = ChatTranslationService.NormalizeLanguage(userCountry);
        var isEnglish = language.Equals("English", StringComparison.OrdinalIgnoreCase);
        var traceId = Guid.NewGuid().ToString("N");
        var decisionSteps = new List<BotDecisionStep>();
        void Step(string key, string? detail = null)
        {
            var step = new BotDecisionStep(key, detail);
            decisionSteps.Add(step);
            onDecisionStep?.Invoke(step);
        }
        BotAnswerResult Complete(BotAnswerResult result)
        {
            var mediaQaIds = result.SearchResult is null ? []
                : BotAnswerMediaSelector.SelectQaEntryIds(question, result.SearchResult.Chunks);
            Step(result.Media.Count > 0 ? "BotTraceMediaAttached" : "BotTraceNoMedia",
                result.Media.Count > 0
                    ? string.Join(", ", mediaQaIds.Select(id => $"QA #{id}")
                        .Concat(result.Media.Select(x => x.OriginalFileName)))
                    : null);
            return result with { DecisionSteps = decisionSteps.ToList() };
        }

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
            Step("BotTraceOperatorRequested");
            await _audit.WriteAsync("Bot.Request.EscalatedByUserTrigger", new { machineId, question }, traceId, cancellationToken);
            return Complete(BotAnswerResult.Escalate(EscalationText(isEnglish)));
        }

        var machine = await _db.Machines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken);
        var turn = await BotTurnClassifier.ClassifyAsync(question, conversationContext, _ollama,
            cancellationToken, machine);
        Step(turn.Intent switch
        {
            BotTurnIntent.NewQuestion => "BotTraceNewQuestion",
            BotTurnIntent.Clarification => "BotTraceClarification",
            BotTurnIntent.Affirmation => "BotTraceAffirmation",
            BotTurnIntent.Negation => "BotTraceNegation",
            _ => "BotTraceUncertainIntent"
        });
        Step(turn.Source == "model" ? "BotTraceIntentFromModel" : "BotTraceIntentFromRules");
        await _audit.WriteAsync("Bot.Turn.Classified", new
        {
            machineId,
            intent = turn.Intent.ToString(),
            turn.Confidence,
            turn.Source
        }, traceId, cancellationToken);
        if (turn.IsReliable && turn.Intent == BotTurnIntent.NewQuestion)
            conversationContext = null;
        var explicitCode = BotTechnicalCode.Extract(question, conversationContext, machine);
        var pendingCode = BotTechnicalCode.GetPendingSuggestedCode(conversationContext);
        var lastReportedCode = BotTechnicalCode.GetLastReportedCode(conversationContext, machine);
        if (explicitCode != null && lastReportedCode != null
            && !explicitCode.Equals(lastReportedCode, StringComparison.OrdinalIgnoreCase)
            && !explicitCode.Equals(pendingCode?.SuggestedCode, StringComparison.OrdinalIgnoreCase))
            conversationContext = null;
        var forceFollowUp = turn.IsReliable && turn.Intent == BotTurnIntent.Clarification ? true : (bool?)null;
        var conversationPlan = BotConversationPlanner.Create(question, conversationContext, forceFollowUp,
            turn.IsReliable ? turn.Intent : BotTurnIntent.Unknown);
        Step(conversationPlan.IsFollowUp ? "BotTraceFollowUpPlan" : "BotTraceNewSearchPlan",
            conversationPlan.SearchQuestion);
        var suggestedCodeReply = BotTechnicalCode.ResolveSuggestedCodeReply(question, conversationContext,
            turn.IsReliable ? turn.Intent : BotTurnIntent.Unknown);
        var currentCode = BotTechnicalCode.Extract(question, conversationContext, machine);
        if (suggestedCodeReply == null && (!turn.IsReliable || turn.Intent == BotTurnIntent.Other)
            && currentCode == null
            && BotTechnicalCode.GetPendingSuggestedCode(conversationContext) is { } pendingSuggestion)
        {
            Step("BotTraceCodeChoiceNeeded");
            var template = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotChooseFaultCode");
            return Complete(new BotAnswerResult(string.Format(template,
                pendingSuggestion.SuggestedCode.ToUpperInvariant(),
                pendingSuggestion.ReportedCode.ToUpperInvariant()), false, []));
        }
        if (suggestedCodeReply is { SelectedCode: null })
        {
            Step("BotTraceCodeRejected");
            var template = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotSuggestedFaultRejected");
            return Complete(new BotAnswerResult(string.Format(template,
                suggestedCodeReply.SuggestedCode.ToUpperInvariant()), false, []));
        }
        var technicalCode = suggestedCodeReply?.SelectedCode
            ?? BotTechnicalCode.ExtractForFollowUp(question, conversationContext, conversationPlan.IsFollowUp, machine);
        if (suggestedCodeReply == null && conversationPlan.NeedsChoiceClarification)
        {
            Step("BotTraceChoiceNeeded");
            return Complete(new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotChooseClarification"), false, []));
        }
        if (suggestedCodeReply == null && conversationPlan.NeedsDetailClarification)
        {
            Step("BotTraceDetailNeeded");
            return Complete(new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotDescribeFollowUp"), false, []));
        }
        if (technicalCode == null && !conversationPlan.IsFollowUp
            && BotQuestionSpecificity.NeedsSymptom(question))
        {
            Step("BotTraceNeedSymptom");
            return Complete(new BotAnswerResult(
                UiText.ForLanguage(isEnglish ? "en" : "ru", "BotNeedSpecificSymptom"), false, []));
        }
        var retrievalQuestion = await BuildRussianRetrievalQuestionAsync(conversationPlan.SearchQuestion, language, traceId, cancellationToken);
        Step(technicalCode == null ? "BotTraceHybridSearch" : "BotTraceExactCodeSearch",
            technicalCode ?? retrievalQuestion);
        var ragResult = await _ragSearch.SearchAsync(new RagSearchRequest
        {
            TraceId = traceId,
            Question = retrievalQuestion,
            MachineId = machineId,
            FinalTopK = 5
        }, cancellationToken);
        if (conversationPlan.IsCorrection && ragResult.ShouldCallOperator)
        {
            var issueDecision = await BotFollowUpQueryRewriter.RewriteAsync(conversationPlan.SearchQuestion, conversationContext,
                _ollama, cancellationToken);
            if (issueDecision?.Match is { } rewritten)
            {
                conversationPlan = conversationPlan with
                {
                    SearchQuestion = rewritten.Query,
                    FocusedSearchQuestion = rewritten.Query,
                    OriginalProblem = rewritten.Query
                };
                retrievalQuestion = await BuildRussianRetrievalQuestionAsync(rewritten.Query, language, traceId, cancellationToken);
                Step("BotTraceQueryRewritten", $"{retrievalQuestion} ({rewritten.Similarity:F2})");
                ragResult = await _ragSearch.SearchAsync(new RagSearchRequest
                {
                    TraceId = traceId,
                    Question = retrievalQuestion,
                    MachineId = machineId,
                    FinalTopK = 5
                }, cancellationToken);
            }
            else if (issueDecision?.Scores.Count > 0)
            {
                Step("BotTraceIssueMatchUncertain", string.Join("; ", issueDecision.Scores.Take(3)
                    .Select(x => $"{x.Issue} ({x.Score:F2})")));
            }
        }
        string? supplementalRetrievalQuestion = null;
        IReadOnlyList<RagChunkCandidate> unmatchedCodeCandidates = [];
        RagChunkCandidate? nearCodeCandidate = null;

        // A combined fault can bury one exact QA below the context window.
        // Retrieve each independently named fault before composing an answer.
        var combinedSegments = Regex.Split(retrievalQuestion, @"\s+и\s+|\s+and\s+", RegexOptions.IgnoreCase);
        if (technicalCode == null && !conversationPlan.IsFollowUp
            && combinedSegments.Length is >= 2 and <= 3
            && BotAnswerMediaSelector.SelectQaEntryIds(retrievalQuestion, ragResult.Chunks).Count >= 2)
        {
            var independentlyMatched = new List<RagChunkCandidate>();
            foreach (var segment in combinedSegments)
            {
                var focused = await _ragSearch.SearchAsync(new RagSearchRequest
                {
                    TraceId = traceId,
                    Question = segment.Trim(),
                    MachineId = machineId,
                    FinalTopK = 8
                }, cancellationToken);
                var qaId = BotAnswerMediaSelector.SelectQaEntryId(segment, focused.Chunks, minimumScore: 2);
                var qa = focused.Chunks.FirstOrDefault(x => x.QAEntryId == qaId);
                if (qa == null) break;
                independentlyMatched.Add(qa);
            }
            if (independentlyMatched.Select(x => x.QAEntryId).Distinct().Count() == combinedSegments.Length)
            {
                ragResult.Chunks = independentlyMatched;
                ragResult.ShouldCallOperator = false;
                supplementalRetrievalQuestion = string.Join(" | ", combinedSegments.Select(x => x.Trim()));
            }
        }

        if (technicalCode != null)
        {
            // A short reply can supply the fault code requested by the bot. Search that
            // identifier directly; broad dialogue queries can bury its manual entry.
            var exact = ragResult.Chunks.Where(x => BotTechnicalCode.Matches(x, technicalCode)).ToList();
            var candidates = ragResult.Chunks.ToList();
            if (exact.Count == 0)
            {
                supplementalRetrievalQuestion = $"ошибка {technicalCode}";
                var codeResult = await _ragSearch.SearchAsync(new RagSearchRequest
                {
                    TraceId = traceId,
                    Question = supplementalRetrievalQuestion,
                    MachineId = machineId,
                    FinalTopK = 8
                }, cancellationToken);
                exact = codeResult.Chunks.Where(x => BotTechnicalCode.Matches(x, technicalCode)).ToList();
                candidates.AddRange(codeResult.Chunks);
                if (exact.Count > 0) ragResult.Confidence = Math.Max(ragResult.Confidence, codeResult.Confidence);
            }

            if (exact.Count == 0)
            {
                var distinctCandidates = candidates.DistinctBy(x => x.ChunkId).ToList();
                nearCodeCandidate = BotTechnicalCode.FindSingleNearMatch(technicalCode, distinctCandidates);
                unmatchedCodeCandidates = (nearCodeCandidate == null
                        ? distinctCandidates
                        : new[] { nearCodeCandidate }.Concat(distinctCandidates.Where(x => x.ChunkId != nearCodeCandidate.ChunkId)))
                    .Take(5).ToList();
            }
            ragResult.Chunks = exact;
            ragResult.ShouldCallOperator = exact.Count == 0;
        }

        var referenceCode = conversationPlan.IsFollowUp
            ? Regex.Match(question, @"\b[A-Za-z]{1,3}\d{2,4}\b", RegexOptions.IgnoreCase).Value
            : string.Empty;
        if (conversationPlan.IsFollowUp && string.IsNullOrEmpty(referenceCode))
            referenceCode = Regex.Match(conversationPlan.SearchQuestion, @"\b[A-Za-z]{1,3}\d{2,4}\b", RegexOptions.IgnoreCase).Value;
        if (technicalCode == null) FilterByReferenceCode(ragResult, referenceCode);
        FilterByResolvedReference(ragResult, conversationPlan.ResolvedReference);
        if (technicalCode == null)
            ragResult.Chunks = BotNumericReferenceFilter.Select(retrievalQuestion, ragResult.Chunks).ToList();
        if (technicalCode == null)
        {
            ragResult.Chunks = BotIdentifierEvidenceGate.Select(retrievalQuestion, machine, ragResult.Chunks).ToList();
            if (ragResult.Chunks.Count == 0) ragResult.ShouldCallOperator = true;
        }
        if (technicalCode == null && !conversationPlan.IsFollowUp)
            ragResult.Chunks = BotMultiIssueContextSelector.Select(question, ragResult.Chunks).ToList();

        if (conversationPlan.AsksAboutPriorAnswer && technicalCode == null)
        {
            var related = BotFollowUpFocus.Select(ragResult.Chunks,
                conversationPlan.OriginalProblem, conversationPlan.LastBotAnswer);
            if (related.Count == 0)
            {
                supplementalRetrievalQuestion = await BuildRussianRetrievalQuestionAsync(
                    $"{conversationPlan.OriginalProblem}. {question}", language, traceId, cancellationToken);
                var focusedResult = await _ragSearch.SearchAsync(new RagSearchRequest
                {
                    TraceId = traceId,
                    Question = supplementalRetrievalQuestion,
                    MachineId = machineId,
                    FinalTopK = 8
                }, cancellationToken);
                related = BotFollowUpFocus.Select(focusedResult.Chunks,
                    conversationPlan.OriginalProblem, conversationPlan.LastBotAnswer);
                ragResult.Confidence = Math.Max(ragResult.Confidence, focusedResult.Confidence);
                ragResult.ShouldCallOperator = focusedResult.ShouldCallOperator;
            }
            ragResult.Chunks = related.ToList();
            if (related.Count == 0) ragResult.ShouldCallOperator = true;
        }

        var evidenceText = conversationPlan.IsCorrection
            ? conversationPlan.SearchQuestion : conversationPlan.ResolvedReference ?? question;
        var evidenceQuestion = conversationPlan.IsFollowUp && isEnglish
            ? await _translation.TranslateToRussianForSearchAsync(evidenceText, language, cancellationToken)
            : evidenceText;
        var missingTerms = conversationPlan.IsFollowUp && technicalCode == null
            && !conversationPlan.AnswersBotQuestion && !conversationPlan.AsksAboutPriorAnswer
            ? BotEvidenceGate.MissingTerms(evidenceQuestion, ragResult.Chunks)
            : [];
        if (conversationPlan.IsFollowUp && technicalCode == null && !conversationPlan.AsksAboutPriorAnswer
            && (ragResult.ShouldCallOperator || missingTerms.Count > 0))
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
            FilterByResolvedReference(focusedResult, conversationPlan.ResolvedReference);
            var merged = focusedResult.Chunks.Concat(ragResult.Chunks)
                .DistinctBy(x => x.ChunkId).Take(8).ToList();
            merged = BotIdentifierEvidenceGate.Select(retrievalQuestion, machine, merged).ToList();
            ragResult.Chunks = merged;
            ragResult.ShouldCallOperator = merged.Count == 0
                || (ragResult.ShouldCallOperator && focusedResult.ShouldCallOperator);
            ragResult.Confidence = Math.Max(ragResult.Confidence, focusedResult.Confidence);
            missingTerms = conversationPlan.AnswersBotQuestion ? [] : BotEvidenceGate.MissingTerms(evidenceQuestion, merged);
        }

        _logger.LogInformation(
            "Bot RAG result. MachineId={MachineId}, Chunks={Chunks}, Confidence={Confidence:F3}, ShouldCallOperator={ShouldCallOperator}, Warning={Warning}",
            machineId,
            ragResult.Chunks.Count,
            ragResult.Confidence,
            ragResult.ShouldCallOperator,
            ragResult.Warning);
        if (!string.IsNullOrWhiteSpace(supplementalRetrievalQuestion))
            Step("BotTraceAdditionalSearch", supplementalRetrievalQuestion);
        Step(ragResult.Chunks.Count == 0 ? "BotTraceNoSources" : "BotTraceSources",
            ragResult.Chunks.Count == 0 ? null : string.Join("; ", ragResult.Chunks.Take(5).Select(x =>
                $"{(x.SourceType == "ResolvedTicket" ? UiText.ForLanguage(isEnglish ? "en" : "ru", "BotTraceSourceChat") : x.DocumentType == "QA" ? "QA" : UiText.ForLanguage(isEnglish ? "en" : "ru", "BotTraceSourceDocument"))} #{x.ChunkId} {x.Title ?? x.DocumentName ?? x.SectionTitle ?? x.ErrorCode} ({x.RerankScore:F3})")));

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

            if (technicalCode != null && ragResult.Chunks.Count == 0)
            {
                var priorMentions = BotTechnicalCode.CountConsecutivePriorMentions(technicalCode, conversationContext, machine);
                var near = nearCodeCandidate;
                var key = near != null
                    ? priorMentions > 0 ? "BotFaultCodeNearMatchRepeated" : "BotFaultCodeNearMatch"
                    : priorMentions > 0 ? "BotFaultCodeStillNotFound" : "BotFaultCodeNotFound";
                var template = UiText.ForLanguage(isEnglish ? "en" : "ru", key);
                var nearDescription = near?.Text.Trim() ?? string.Empty;
                if (near != null && isEnglish)
                {
                    var translated = await _translation.TranslateAsync(nearDescription, "Russian", language, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(translated)) nearDescription = translated;
                }
                var reply = near != null
                    ? string.Format(template, technicalCode.ToUpperInvariant(), near.ErrorCode!.ToUpperInvariant(), nearDescription)
                    : string.Format(template, technicalCode.ToUpperInvariant());
                Step(near != null ? "BotTraceNearCode" : "BotTraceCodeNotFound", technicalCode);
                return Complete(new BotAnswerResult(reply, false, [])
                { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion,
                    SearchResult = ragResult, UnmatchedCodeCandidates = unmatchedCodeCandidates });
            }

            Step("BotTraceClarificationFromSearch");
            return Complete(new BotAnswerResult(
                await BuildClarificationAnswerAsync(ragResult, language, isEnglish, cancellationToken),
                false,
                []) { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult });
        }

        if (conversationPlan.IsFollowUp && missingTerms.Count > 0)
        {
            Step("BotTraceEvidenceMissing", string.Join(", ", missingTerms));
            return Complete(new BotAnswerResult(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotFollowUpEvidenceMissing"), false, [])
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = _ragSearch.BuildContextForLlm(ragResult) });
        }

        if (BotQaAmbiguity.GetChoices(question, ragResult.Chunks, conversationPlan.IsFollowUp) is { } choices)
        {
            Step("BotTraceChoiceNeeded", $"{choices.First}; {choices.Second}");
            var reply = string.Format(UiText.ForLanguage(isEnglish ? "en" : "ru", "BotQaAmbiguityQuestion"),
                choices.First, choices.Second);
            return Complete(new BotAnswerResult(reply, false, [])
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult });
        }

        // A correction can reject part of the literal message. Match attachments to
        // the issue that was actually searched, so rejected causes cannot win.
        var mediaQuestion = conversationPlan.IsCorrection ? conversationPlan.SearchQuestion : question;
        mediaQuestion = BotMachineMention.Remove(mediaQuestion, machine);
        var answerMedia = await LoadAnswerMediaAsync(mediaQuestion, ragResult,
            conversationPlan.IsFollowUp && !BotFollowUpMediaPolicy.RequestsVisual(question) ? 2 : 1,
            cancellationToken);
        if (!BotFollowUpMediaPolicy.ShouldAttach(question, conversationPlan)) answerMedia = [];
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

        var independentQaIds = BotAnswerMediaSelector.SelectQaEntryIds(mediaQuestion, ragResult.Chunks);
        if (independentQaIds.Count >= 2)
        {
            var combinedAnswer = await BuildDeterministicAnswerFromContextAsync(
                ragResult, language, isEnglish, cancellationToken, question);
            if (!string.IsNullOrWhiteSpace(combinedAnswer))
            {
                Step("BotTraceMultiQa", string.Join(", ", independentQaIds));
                return Complete(new BotAnswerResult(combinedAnswer, false, answerMedia)
                { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion,
                    SearchResult = ragResult, ContextForLlm = context });
            }
        }

        // Prepared manuals carry one verified, self-contained entry per fault code.
        // When exactly one applicable entry matches, return it directly so a previous
        // "not found" answer cannot override newly available evidence.
        if (technicalCode != null && ragResult.Chunks.Count == 1
            && string.Equals(ragResult.Chunks[0].ErrorCode, technicalCode, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ragResult.Chunks[0].Text))
        {
            var documented = ragResult.Chunks[0].Text.Trim();
            if (isEnglish)
            {
                var translated = await _translation.TranslateAsync(documented, "Russian", language, cancellationToken);
                if (!string.IsNullOrWhiteSpace(translated)) documented = translated;
            }
            Step("BotTraceDirectManual", $"#{ragResult.Chunks[0].ChunkId}");
            return Complete(new BotAnswerResult(documented, false, answerMedia)
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context });
        }

        if (conversationPlan.IsCorrection && ragResult.Chunks.Count == 1)
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
                    if (technicalCode == null && !conversationPlan.AsksAboutPriorAnswer)
                        directAnswer = BotResponseDeduplicator.RemoveRepeatedSteps(directAnswer.Trim(), conversationPlan.PreviousBotAnswer);
                    if (string.IsNullOrWhiteSpace(directAnswer))
                        directAnswer = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotNoNewSteps");
                    if (!string.IsNullOrWhiteSpace(conversationPlan.ResolvedReference)
                        && !directAnswer.Contains(conversationPlan.ResolvedReference, StringComparison.OrdinalIgnoreCase))
                        directAnswer = $"{conversationPlan.ResolvedReference}: {directAnswer}";
                    Step("BotTraceDirectQa", $"#{exactQa.ChunkId}");
                    return Complete(new BotAnswerResult(directAnswer, false, answerMedia)
                    { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context });
                }
            }
        }

        if (!conversationPlan.IsFollowUp && BotExactQaSelector.Select(question, ragResult.Chunks) is { } matchingQa)
        {
            var directAnswer = matchingQa.Solution!.Trim();
            if (isEnglish)
            {
                var translated = await _translation.TranslateAsync(directAnswer, "Russian", language, cancellationToken);
                if (!string.IsNullOrWhiteSpace(translated)) directAnswer = translated;
            }
            Step("BotTraceDirectQa", $"#{matchingQa.ChunkId}");
            return Complete(new BotAnswerResult(directAnswer, false, answerMedia)
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion,
                SearchResult = ragResult, ContextForLlm = context });
        }

        var promptHistory = conversationPlan.IsFollowUp
            ? BuildPromptHistory(conversationContext)
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
        - Если клиент спрашивает, что именно означает предмет или действие из предыдущего ответа, объясни именно этот предмет в рамках исходной неисправности. Не примешивай похожие инструкции других узлов. Если найденный источник не уточняет цвет, номер или расположение, прямо скажи об этом.
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

        Step("BotTraceModelAnswer", $"{ragResult.Chunks.Count}");
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

                Step("BotTraceModelFallbackQa");
                return Complete(new BotAnswerResult(deterministicAnswer, false, answerMedia)
                { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context, LlmInvoked = true });
            }

            var fallback = isEnglish
                ? "I found context, but could not form a reliable answer. Please clarify the machine unit, error text, sensor number, or what happens on the screen. How else can I help? If you need an operator, write \"operator\" in the chat."
                : "Контекст найден, но я не смог сформировать надежный ответ. Уточните узел станка, текст ошибки, номер датчика или что именно происходит на экране. Чем могу помочь ещё? Если нужен оператор, напишите в чат: оператор.";
            Step("BotTraceModelUnavailable");
            return Complete(new BotAnswerResult(fallback, false, [])
            { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context, LlmInvoked = true });
        }

        var cleanAnswer = CleanHumanAnswer(answer.Trim());
        var usedGroundedFallback = false;
        var strongestEvidence = ragResult.Chunks.FirstOrDefault()?.Solution
            ?? ragResult.Chunks.FirstOrDefault()?.Text;
        if (BotAnswerGroundingGuard.HasUnsupportedClaims(cleanAnswer, strongestEvidence ?? string.Empty))
        {
            string? groundedAnswer;
            if (conversationPlan.IsFollowUp)
            {
                groundedAnswer = BotEvidenceExcerptSelector.Select(strongestEvidence ?? string.Empty, question, cleanAnswer);
                if (isEnglish && !string.IsNullOrWhiteSpace(groundedAnswer))
                    groundedAnswer = await _translation.TranslateAsync(groundedAnswer, "Russian", language, cancellationToken);
            }
            else
            {
                groundedAnswer = await BuildDeterministicAnswerFromContextAsync(ragResult, language, isEnglish, cancellationToken);
            }
            cleanAnswer = !string.IsNullOrWhiteSpace(groundedAnswer)
                ? groundedAnswer
                : UiText.ForLanguage(isEnglish ? "en" : "ru", "BotGroundingNotFound");
            usedGroundedFallback = true;
            if (string.IsNullOrWhiteSpace(groundedAnswer)) answerMedia = [];
            Step("BotTraceGroundingFallback");
        }
        if (conversationPlan.IsFollowUp && technicalCode == null && !conversationPlan.AsksAboutPriorAnswer
            && !usedGroundedFallback)
        {
            var withoutRepeats = BotResponseDeduplicator.RemoveRepeatedSteps(cleanAnswer, conversationPlan.PreviousBotAnswer);
            if (withoutRepeats != cleanAnswer) Step("BotTraceRepeatedStepsRemoved");
            cleanAnswer = withoutRepeats;
            if (string.IsNullOrWhiteSpace(cleanAnswer))
            {
                cleanAnswer = UiText.ForLanguage(isEnglish ? "en" : "ru", "BotNoNewSteps");
            }
        }
        if (LooksLikeSourceOnlyAnswer(cleanAnswer))
        {
            Step("BotTraceSourceOnlyAnswer");
            cleanAnswer = await BuildClarificationAnswerAsync(ragResult, language, isEnglish, cancellationToken);
            answerMedia = [];
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

        Step(mustEscalate ? "BotTraceOperatorHandoff" : "BotTraceAnswerReady");
        return Complete(new BotAnswerResult(cleanAnswer, mustEscalate, answerMedia)
        { RetrievalQuestion = retrievalQuestion, SupplementalRetrievalQuestion = supplementalRetrievalQuestion, SearchResult = ragResult, ContextForLlm = context, LlmInvoked = true });
    }

    private async Task<string?> BuildDeterministicAnswerFromContextAsync(
        RagSearchResult ragResult,
        string language,
        bool isEnglish,
        CancellationToken cancellationToken,
        string? question = null)
    {
        var qaCandidates = ragResult.Chunks
            .Where(x => string.Equals(x.DocumentType, "QA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.QAStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Solution))
            .OrderByDescending(x => x.RerankScore)
            .ToList();

        var qaIds = !string.IsNullOrWhiteSpace(question)
            ? BotAnswerMediaSelector.SelectQaEntryIds(question, qaCandidates)
            : [];
        var selectedQa = qaIds.Count >= 2
            ? qaCandidates.Where(x => x.QAEntryId.HasValue && qaIds.Contains(x.QAEntryId.Value)).ToList()
            : qaCandidates.Take(1).ToList();

        var answer = selectedQa.Count >= 2
            ? string.Join("\n\n", selectedQa.Select(x => $"{x.Title?.TrimEnd(' ', '?')}:\n{x.Solution!.Trim()}"))
            : selectedQa.FirstOrDefault()?.Solution?.Trim();
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

    private async Task<IReadOnlyList<BotAnswerMedia>> LoadAnswerMediaAsync(
        string question, RagSearchResult ragResult, int minimumScore, CancellationToken cancellationToken)
    {
        var qaIds = BotAnswerMediaSelector.SelectQaEntryIds(question, ragResult.Chunks, minimumScore);
        if (qaIds.Count == 0)
        {
            return [];
        }

        return await _db.QAAttachments
            .AsNoTracking()
            .Where(x => qaIds.Contains(x.QAEntryId)
                && x.QAEntry != null
                && x.QAEntry.Status == QAEntryStatuses.Verified)
            .OrderBy(x => x.Id)
            .Take(12)
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

    private static string BuildPromptHistory(string? context)
    {
        var turns = BotConversationHistory.Parse(context).TakeLast(4).ToList();
        return string.Join("\n", turns.Select((turn, index) =>
        {
            var limit = index == turns.Count - 1 && turn.Role == "Бот" ? 4000 : 700;
            var text = turn.Text.Length <= limit ? turn.Text : turn.Text[..limit] + "...";
            return $"{turn.Role}: {text}";
        }));
    }

    private static void FilterByReferenceCode(RagSearchResult result, string referenceCode)
    {
        if (string.IsNullOrWhiteSpace(referenceCode)) return;
        result.Chunks = result.Chunks.Where(x =>
            string.Concat(x.Title, " ", x.Text, " ", x.Solution)
                .Contains(referenceCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (result.Chunks.Count == 0) result.ShouldCallOperator = true;
    }

    private static void FilterByResolvedReference(RagSearchResult result, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return;
        var target = Regex.Replace(reference, @"[\s\p{P}]+", "");
        result.Chunks = result.Chunks.Where(x =>
            Regex.Replace(string.Concat(x.Title, " ", x.Text, " ", x.Solution), @"[\s\p{P}]+", "")
                .Contains(target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (result.Chunks.Count == 0) result.ShouldCallOperator = true;
    }

    private async Task<string> BuildClarificationAnswerAsync(
        RagSearchResult ragResult,
        string language,
        bool isEnglish,
        CancellationToken cancellationToken)
    {
        var similar = ragResult.Chunks
            .Where(x => x.RerankScore >= 0.25)
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
    public IReadOnlyList<BotDecisionStep> DecisionSteps { get; init; } = [];
    public string? RetrievalQuestion { get; init; }
    public string? SupplementalRetrievalQuestion { get; init; }
    public RagSearchResult? SearchResult { get; init; }
    public string? ContextForLlm { get; init; }
    public bool LlmInvoked { get; init; }
    public IReadOnlyList<RagChunkCandidate> UnmatchedCodeCandidates { get; init; } = [];
    public static BotAnswerResult Escalate(string text) => new(text, true, []);
}

public sealed record BotDecisionStep(string Key, string? Detail = null);

public sealed record BotAnswerMedia(
    Guid PublicId,
    string OriginalFileName,
    string StoredFileName,
    string FilePath,
    string StorageProvider,
    string ContentType,
    long SizeBytes);
