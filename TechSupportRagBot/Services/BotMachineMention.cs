using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

/// <summary>Removes the selected machine's designation from attachment matching terms.</summary>
public static class BotMachineMention
{
    public static string Remove(string question, Machine? machine)
    {
        if (machine is null) return question;
        var serial = machine.SerialNumber?.Trim();
        var aliases = new[] { machine.Model, machine.Name }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => new[] { x!, string.IsNullOrWhiteSpace(serial) ? null : $"{x}-{serial}",
                string.IsNullOrWhiteSpace(serial) ? null : $"{x} {serial}" })
            .Where(x => x is { Length: >= 3 } && x.Any(char.IsDigit))
            .OrderByDescending(x => x!.Length)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
            question = Regex.Replace(question, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias!)}(?![\p{{L}}\p{{N}}])",
                " ", RegexOptions.IgnoreCase);
        return Regex.Replace(question, @"\s+", " ").Trim();
    }
}
