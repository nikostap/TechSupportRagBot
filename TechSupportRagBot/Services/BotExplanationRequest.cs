using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Recognizes a request to rephrase the previous answer without adding a new machine symptom.</summary>
public static partial class BotExplanationRequest
{
    public static bool IsPureRequest(string text)
    {
        var normalized = Regex.Replace(text.Trim().ToLowerInvariant(), @"[\s,;.!?]+", " ").Trim();
        return Request().IsMatch(normalized);
    }

    [GeneratedRegex(@"^(?:(?:я|мы)\s+)?(?:не\s+понял(?:а|и)?|не\s+понятно|неясно)(?:\s+(?:можешь|можете))?\s*(?:поясни(?:те)?|объясни(?:те)?|разъясни(?:те)?)?\s*(?:пожалуйста|подробнее|попроще|простыми\s+словами)?$|^(?:поясни(?:те)?|объясни(?:те)?|разъясни(?:те)?|расскажи(?:те)?)\s*(?:ещ[её]\s+раз|подробнее|попроще|простыми\s+словами|пожалуйста)?$|^(?:можно|можешь|можете)\s+(?:попроще|подробнее|объяснить\s+(?:попроще|подробнее|ещ[её]\s+раз))$|^(?:i\s+don'?t\s+understand|please\s+explain|explain\s+(?:again|more\s+simply)|could\s+you\s+explain(?:\s+again)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Request();
}
