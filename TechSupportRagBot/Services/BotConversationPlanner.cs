namespace TechSupportRagBot.Services;

/// <summary>Builds bounded retrieval queries from the current issue and its client-provided details.</summary>
public static class BotConversationPlanner
{
    private static readonly string[] References =
    [
        "это", "этот", "эта", "эти", "его", "ее", "её", "он", "она", "они", "тот", "там",
        "такой", "такая", "this", "that", "it", "same", "previous"
    ];

    public static BotConversationPlan Create(string question, string? history)
    {
        question = question.Trim();
        var lines = (history ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var clientTurns = lines.Where(x => x.StartsWith("Клиент: ", StringComparison.Ordinal)).Select(x => x[8..]).ToList();
        var previousClient = clientTurns.LastOrDefault();
        var previousBot = lines.LastOrDefault(x => x.StartsWith("Бот: ", StringComparison.Ordinal))?[5..];
        var previousAnswers = string.Join(" ", lines.Where(x => x.StartsWith("Бот: ", StringComparison.Ordinal)).Select(x => x[5..]));
        var hasExchange = !string.IsNullOrWhiteSpace(previousClient) && !string.IsNullOrWhiteSpace(previousBot);
        var normalized = question.ToLowerInvariant();
        var isConfirmation = normalized is "да" or "нет" or "ага" or "угу" or "верно" or "правильно"
            or "не то" or "не это" or "yes" or "no" or "yeah" or "yep" or "correct";
        var hasReference = References.Any(x => ContainsWord(normalized, x));
        var newQuestion = normalized.TrimStart('а', ' ', ',');
        var startsNewQuestion = new[] { "как ", "почему ", "можно ", "что делать", "где ", "какой ", "какое ", "какая ", "какие ", "какую ", "сколько ", "how ", "why ", "where ", "what ", "which " }
            .Any(x => newQuestion.StartsWith(x, StringComparison.Ordinal));
        var answersBotQuestion = (previousBot?.Contains('?') == true
                || previousBot?.Contains("уточните", StringComparison.OrdinalIgnoreCase) == true
                || previousBot?.Contains("please specify", StringComparison.OrdinalIgnoreCase) == true)
            && question.Length <= 100 && !startsNewQuestion;
        var isShortDetail = question.Length <= 100 && !startsNewQuestion && !question.Contains('?');
        var isFollowUp = hasExchange && (isConfirmation || hasReference || answersBotQuestion || isShortDetail
            || (startsNewQuestion && SharesTechnicalTerm(previousClient!, question)));
        var lastQuestionMark = previousBot?.LastIndexOf('?') ?? -1;
        var lastBotQuestion = lastQuestionMark >= 0
            ? previousBot![Math.Max(0, lastQuestionMark - 160)..(lastQuestionMark + 1)]
            : string.Empty;
        var needsChoice = isConfirmation && normalized is not ("нет" or "no" or "не то" or "не это")
            && (lastBotQuestion.Contains(" или ", StringComparison.OrdinalIgnoreCase)
                || (lastBotQuestion.Contains('(') && lastBotQuestion.Contains(", ", StringComparison.OrdinalIgnoreCase)));
        var needsDetail = isConfirmation && normalized is not ("нет" or "no" or "не то" or "не это")
            && (lastBotQuestion.Length == 0
                || lastBotQuestion.Contains("чем могу помочь", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("ещё", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("еще", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("нужно уточнить", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("признак", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("симптом", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("anything else", StringComparison.OrdinalIgnoreCase)
                || lastBotQuestion.Contains("additional", StringComparison.OrdinalIgnoreCase));

        if (!isFollowUp)
        {
            return new BotConversationPlan(question, question, false, string.Empty, false, false);
        }

        // Keep every recent client clarification belonging to this issue. Bot answers can ask
        // for details, but must never become search facts. The prompt budget stays bounded.
        var issueStart = clientTurns.FindLastIndex(LooksLikeIndependentQuestion);
        if (issueStart < 0) issueStart = 0;
        var originalProblem = clientTurns[issueStart];
        var resolvedReference = BotOrdinalReference.Resolve(question, previousBot);
        var details = clientTurns.Skip(issueStart + 1).Append(question)
            .Where(x => !IsBareConfirmation(x))
            .TakeLast(4)
            .Select(x => Truncate(x.Trim(), 90))
            .ToArray();
        var detailText = Truncate(string.Join("; ", details), 190);
        if (resolvedReference is not null) detailText = $"{detailText}; {resolvedReference}";
        var searchQuestion = $"{Truncate(originalProblem, 190)}. Уточнения клиента: {detailText}";
        var focusedSearchQuestion = $"{detailText}. {Truncate(originalProblem, 160)}";
        return new BotConversationPlan(searchQuestion, focusedSearchQuestion, true, Truncate(previousAnswers, 1400), needsChoice, needsDetail, resolvedReference);
    }

    private static bool LooksLikeIndependentQuestion(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text.Length > 30 && !(References.Any(x => ContainsWord(text, x)) || text is "да" or "нет" or "yes" or "no");
    }

    private static bool IsBareConfirmation(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text is "да" or "нет" or "ага" or "угу" or "верно" or "правильно"
            or "yes" or "no" or "yeah" or "yep" or "correct";
    }

    private static bool SharesTechnicalTerm(string previous, string current)
    {
        static HashSet<string> Terms(string value) => value.ToLowerInvariant()
            .Split(value.Where(x => !char.IsLetter(x)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 5 && x is not ("станка" or "станке" or "станок" or "machine"))
            .Select(x => x[..4]).ToHashSet();
        return Terms(previous).Overlaps(Terms(current));
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + word.Length;
            if ((index == 0 || !char.IsLetterOrDigit(text[index - 1]))
                && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

public sealed record BotConversationPlan(string SearchQuestion, string FocusedSearchQuestion, bool IsFollowUp, string PreviousBotAnswer, bool NeedsChoiceClarification, bool NeedsDetailClarification, string? ResolvedReference = null);
