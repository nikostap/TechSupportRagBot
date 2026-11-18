using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public static class PositionKeySearch
{
    public static IReadOnlyList<ResolvedComponent> Match(
        string question, IReadOnlyList<ResolvedComponent> installed)
    {
        return installed.Where(item => MatchesKey(question, item.Position.Key)).ToList();
    }

    public static bool MatchesKey(string question, string key)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(key)) return false;
        if (char.IsDigit(key[0])
            && Regex.IsMatch(question, @"\b(ошибка|ошибки|код ошибки|авария|fault|alarm|error)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !Regex.IsMatch(question, @"\b(датчик|датчика|датчике|позиция|позиции|позицию|sensor|position)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        // A shared numeric designation (75.fiber, 75.amplifier) names both parts
        // when the operator asks about position 75.
        var designation = key.Split('.', 2)[0];
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(key)}(?![\p{{L}}\p{{N}}])";
        if (Regex.IsMatch(question, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (!key.Contains('.') || !designation.All(char.IsDigit)) return false;
        pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(designation)}(?![\p{{L}}\p{{N}}.])";
        return Regex.IsMatch(question, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
