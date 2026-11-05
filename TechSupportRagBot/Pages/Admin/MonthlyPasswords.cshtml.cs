using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class MonthlyPasswordsModel : PageModel
{
    private readonly SystemSettingsService _settings;
    public MonthlyPasswordsModel(SystemSettingsService settings) => _settings = settings;
    [BindProperty(SupportsGet = true)] public string? ModelName { get; set; }
    [BindProperty(SupportsGet = true)] public string? SerialNumber { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }

    public string? Result { get; private set; }
    public string? ResultMonth { get; private set; }
    public string? ErrorKey { get; private set; }
    public IReadOnlyList<string> Models { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var modelNames = await _settings.GetMachineModelsAsync(HttpContext.RequestAborted);
        var modelIds = await _settings.GetMonthlyPasswordModelIdsAsync(HttpContext.RequestAborted);
        Models = modelNames.Concat(["ALF-506"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => modelIds.ContainsKey(MonthlyPasswordGenerator.PasswordModelCode(x)))
            .ToList();
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        Year ??= today.Year;
        Month ??= today.Month;
        if (string.IsNullOrWhiteSpace(ModelName) && string.IsNullOrWhiteSpace(SerialNumber)) return;
        if (!Models.Contains(ModelName, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(SerialNumber)
            || Year is < 1 or > 9999 || Month is < 1 or > 12)
        {
            ErrorKey = "MonthlyPasswordInvalidInput";
            return;
        }
        var code = MonthlyPasswordGenerator.PasswordModelCode(ModelName!);
        try
        {
            Result = MonthlyPasswordGenerator.Generate(modelIds[code], code,
                SerialNumber.Trim(), Year!.Value, Month!.Value);
            var culture = CultureInfo.GetCultureInfo(HttpContext.Request.Cookies["lang"] == "en" ? "en-US" : "ru-RU");
            ResultMonth = new DateTime(Year.Value, Month.Value, 1).ToString("MMMM yyyy", culture);
        }
        catch (InvalidOperationException) { ErrorKey = "MonthlyPasswordLegacy"; }
        catch (ArgumentException) { ErrorKey = "MonthlyPasswordInvalidInput"; }
    }
}
