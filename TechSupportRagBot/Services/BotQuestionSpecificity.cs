using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Asks for an observable symptom when a new report names no fault, part or operation.</summary>
public static class BotQuestionSpecificity
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "стан", "маши", "рабо", "дела", "помо", "плох", "хоро", "вооб", "ошиб",
        "вопр", "проб", "сист", "запу", "вклю", "откл", "пише", "пока", "чего", "датч",
        "слом", "штук", "пере", "этой", "само", "what", "mach", "work", "help", "prob", "erro", "star", "stop"
    };

    public static bool NeedsSymptom(string question)
    {
        if (Regex.IsMatch(question, @"\b[A-Za-z]{1,5}[A-Za-z0-9._-]*\d[A-Za-z0-9._-]*\b")
            || Regex.IsMatch(question, @"(?<!\d)\d{2,3}(?!\d)"))
            return false;
        var terms = Regex.Matches(question.ToLowerInvariant(), @"[\p{L}]{4,}")
            .Select(match => match.Value[..4])
            .Where(term => !Generic.Contains(term))
            .Distinct()
            .ToList();
        return terms.Count == 0;
    }
}
