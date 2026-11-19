using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static partial class BotTechnicalTranslationGuard
{
    public static bool IsReliableEnglish(string source, string? translated)
    {
        if (string.IsNullOrWhiteSpace(translated) || Cyrillic().IsMatch(translated)) return false;
        var sourceNumbers = Numbers().Matches(source).Select(x => x.Value.Replace(',', '.')).ToHashSet();
        var translatedNumbers = Numbers().Matches(translated).Select(x => x.Value.Replace(',', '.')).ToHashSet();
        return sourceNumbers.SetEquals(translatedNumbers);
    }

    [GeneratedRegex(@"[\u0400-\u04FF]")]
    private static partial Regex Cyrillic();

    [GeneratedRegex(@"(?<!\d)\d+(?:[.,]\d+)?(?!\d)")]
    private static partial Regex Numbers();
}
