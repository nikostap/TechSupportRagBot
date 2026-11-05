using System.Globalization;
using System.Text.RegularExpressions;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public static class MonthlyPasswordGenerator
{
    public static readonly IReadOnlyList<(int Id, string Code)> DefaultModels =
    [
        (0, "ALF"), (1, "M"), (2, "S"), (3, "L"), (4, "CL"), (5, "CF"),
        (6, "PS"), (7, "CS2"), (8, "PM"), (9, "PB"), (10, "EVO"), (11, "PRO")
    ];

    private static readonly IReadOnlyDictionary<string, int> MinimumNumbers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["M"] = 288, ["PRO"] = 17, ["CL"] = 26, ["MS"] = 74, ["L"] = 233 };

    public static string Generate(int modelId, string modelCode, string serialNumber, int year, int month)
    {
        if (modelId is < 0 or > 99 || string.IsNullOrWhiteSpace(modelCode)
            || !Regex.IsMatch(serialNumber, "^[0-9]{1,3}$") || year < 1 || month is < 1 or > 12)
            throw new ArgumentException("Invalid monthly password parameters.");
        var number = int.Parse(serialNumber, CultureInfo.InvariantCulture);
        if (MinimumNumbers.TryGetValue(modelCode, out var minimum) && number < minimum)
            throw new InvalidOperationException("Legacy machine does not use monthly passwords.");

        // Fixed site-side calculation; model IDs are stored in the site's settings.
        var combinationId = modelId * 1000 + number;
        var calendarIndex = year * 12 + month - 1;
        var calculationBase = (combinationId + calendarIndex) % 100000;
        return ((calculationBase * 7919) % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool TryForMachine(Machine machine, DateOnly month,
        IReadOnlyDictionary<string, int> modelIds, out string password)
    {
        password = string.Empty;
        var code = PasswordModelCode(machine.Model);
        if (!modelIds.TryGetValue(code, out var id)) return false;
        try
        {
            password = Generate(id, code, machine.SerialNumber.Trim(), month.Year, month.Month);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public static string PasswordModelCode(string model) =>
        model.Trim().Equals("ALF-506", StringComparison.OrdinalIgnoreCase)
            ? "ALF" : model.Trim().ToUpperInvariant();
}
