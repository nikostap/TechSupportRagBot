using System.ComponentModel.DataAnnotations;
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
    [BindProperty(SupportsGet = true)] public int? ModelId { get; set; }
    [BindProperty(SupportsGet = true)] public string? SerialNumber { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }

    public string? Result { get; private set; }
    public string? ErrorKey { get; private set; }
    public IReadOnlyList<(int Id, string Code)> Models { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var modelNames = await _settings.GetMachineModelsAsync(HttpContext.RequestAborted);
        var modelIds = await _settings.GetMonthlyPasswordModelIdsAsync(HttpContext.RequestAborted);
        Models = modelNames.Where(x => modelIds.ContainsKey(x))
            .Select(x => (modelIds[x], x)).OrderBy(x => x.Item1).ToList();
        var nextMonth = DateTime.Today.AddMonths(1);
        Year ??= nextMonth.Year;
        Month ??= nextMonth.Month;
        if (ModelId == null && string.IsNullOrWhiteSpace(SerialNumber)) return;
        var model = Models.FirstOrDefault(x => x.Id == ModelId);
        if (model.Code == null || string.IsNullOrWhiteSpace(SerialNumber))
        {
            ErrorKey = "MonthlyPasswordInvalidInput";
            return;
        }
        try
        {
            Result = MonthlyPasswordGenerator.Generate(model.Id, model.Code,
                SerialNumber.Trim(), Year.Value, Month.Value);
        }
        catch (InvalidOperationException) { ErrorKey = "MonthlyPasswordLegacy"; }
        catch (ArgumentException) { ErrorKey = "MonthlyPasswordInvalidInput"; }
    }
}
