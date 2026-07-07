using System.Text;
using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class TextEncodingRepairService
{
    private static readonly Regex MojibakeRegex = new(@"[РС][\u0080-\u00BFА-Яа-я]{2,}", RegexOptions.Compiled);

    public static string RepairIfNeeded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeMojibake(text))
        {
            return text ?? string.Empty;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = Encoding.GetEncoding(1251).GetBytes(text);
            var repaired = Encoding.UTF8.GetString(bytes);

            return LooksLikeMojibake(repaired) ? text : repaired;
        }
        catch
        {
            return text;
        }
    }

    private static bool LooksLikeMojibake(string text)
    {
        return text.Contains('Р') && MojibakeRegex.IsMatch(text);
    }
}
