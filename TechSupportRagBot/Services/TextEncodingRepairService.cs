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
            // Ordinary Cyrillic can resemble the mojibake prefix. Reject any conversion
            // that cannot round-trip through Windows-1251 and strict UTF-8 without loss.
            var bytes = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(text);
            var repaired = new UTF8Encoding(false, true).GetString(bytes);

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
