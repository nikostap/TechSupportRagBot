using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Resolves a short ordinal answer to a numbered item explicitly named by the bot.</summary>
public static partial class BotOrdinalReference
{
    private static readonly string[][] OrdinalStems =
    [
        ["перв", "first"], ["втор", "second"], ["трет", "треть", "third"],
        ["четвер", "четвёр", "fourth"], ["пят", "fifth"], ["шест", "sixth"],
        ["седьм", "seventh"], ["восьм", "eighth"], ["девят", "ninth"],
        ["десят", "tenth"]
    ];

    public static string? Resolve(string answer, string? previousBotAnswer)
    {
        var number = ParseShortOrdinal(answer);
        if (number is null || string.IsNullOrWhiteSpace(previousBotAnswer)) return null;

        return NamedNumber().Matches(previousBotAnswer).Select(x => x.Value)
            .FirstOrDefault(x => int.TryParse(Digits().Match(x).Value, out var found) && found == number);
    }

    private static int? ParseShortOrdinal(string answer)
    {
        var value = answer.Trim().ToLowerInvariant();
        if (value.Length > 20 || value.Contains(' ')) return null;
        var digits = Digits().Match(value);
        if (digits.Success && (value == $"№{digits.Value}" || OrdinalDigit().IsMatch(value))
            && int.TryParse(digits.Value, out var numeric) && numeric is >= 1 and <= 10)
            return numeric;

        for (var index = 0; index < OrdinalStems.Length; index++)
            if (OrdinalStems[index].Any(stem => value.StartsWith(stem, StringComparison.Ordinal))) return index + 1;
        return null;
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[\p{L}]{2,}|[A-Z])[\s-]*№?\s*0?[1-9]\d?(?!\d)")]
    private static partial Regex NamedNumber();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^\d+[-–]?[a-zа-яё]{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalDigit();
}
