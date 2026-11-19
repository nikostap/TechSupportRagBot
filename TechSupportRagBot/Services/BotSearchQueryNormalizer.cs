using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

/// <summary>Aligns common translated shop-floor words with the terms used in Russian QA.</summary>
public static partial class BotSearchQueryNormalizer
{
    public static string NormalizeTranslatedRussian(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        query = Machine().Replace(query, "станок");
        query = Spool().Replace(query, "шпули");
        query = Says().Replace(query, "пишет");
        query = CannotSee().Replace(query, "не видит");
        query = CannotSeeSpool().Replace(query, "не видит шпулю");
        return query;
    }

    [GeneratedRegex(@"\bмашин\w*\b", RegexOptions.IgnoreCase)]
    private static partial Regex Machine();

    [GeneratedRegex(@"\bкатушк\w*\b", RegexOptions.IgnoreCase)]
    private static partial Regex Spool();

    [GeneratedRegex(@"\b(?:говорит|сказала|сообщает)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Says();

    [GeneratedRegex(@"\bне\s+может\s+видеть\b", RegexOptions.IgnoreCase)]
    private static partial Regex CannotSee();

    [GeneratedRegex(@"\bне\s+видит\s+шпули\b", RegexOptions.IgnoreCase)]
    private static partial Regex CannotSeeSpool();
}
