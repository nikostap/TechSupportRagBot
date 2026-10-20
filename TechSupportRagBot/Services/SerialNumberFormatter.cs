using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class SerialNumberFormatter
{
    private static readonly Regex NumericSerial = new("^[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex QaRule = new(@"^([0-9]+)(?:\s*-\s*([0-9]+))?$", RegexOptions.Compiled);

    public static string NormalizeMachine(string? value)
    {
        var serial = value?.Trim() ?? string.Empty;
        return NumericSerial.IsMatch(serial) && serial.Length < 3 ? serial.PadLeft(3, '0') : serial;
    }

    public static string? NormalizeQaRule(string? value)
    {
        var serial = value?.Trim();
        if (string.IsNullOrEmpty(serial)) return null;

        var match = QaRule.Match(serial);
        if (!match.Success) return null;

        var from = NormalizeMachine(match.Groups[1].Value);
        return match.Groups[2].Success
            ? $"{from}-{NormalizeMachine(match.Groups[2].Value)}"
            : from;
    }
}
