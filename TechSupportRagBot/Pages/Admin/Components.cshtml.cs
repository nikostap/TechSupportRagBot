using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class ComponentsModel(ApplicationDbContext db, SystemSettingsService settings) : PageModel
{
    [BindProperty] public ComponentInput Component { get; set; } = new();
    [BindProperty] public PositionInput Position { get; set; } = new();
    [BindProperty] public AssignmentInput Assignment { get; set; } = new();
    [TempData] public string? Notice { get; set; }
    public string Tab { get; private set; } = "models";
    public List<ComponentModel> Components { get; private set; } = [];
    public List<MachinePosition> Positions { get; private set; } = [];
    public List<ComponentInstallation> Installations { get; private set; } = [];
    public List<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<string> MachineModels { get; private set; } = [];

    public async Task OnGetAsync(string tab = "models") => await LoadAsync(tab);

    public async Task<IActionResult> OnPostAddComponentAsync()
    {
        if (string.IsNullOrWhiteSpace(Component.Type) || string.IsNullOrWhiteSpace(Component.Manufacturer)
            || string.IsNullOrWhiteSpace(Component.PartNumber))
            return await ErrorAsync("ComponentRequired", "models");
        var manufacturer = Component.Manufacturer.Trim();
        var part = Component.PartNumber.Trim();
        var revision = Component.Revision?.Trim();
        if (await db.ComponentModels.AnyAsync(x => x.Manufacturer.ToLower() == manufacturer.ToLower()
            && x.PartNumber.ToLower() == part.ToLower() && x.Revision == revision))
            return await ErrorAsync("ComponentDuplicate", "models");
        db.ComponentModels.Add(new ComponentModel
        {
            Type = Component.Type.Trim(), Manufacturer = manufacturer, Series = Component.Series.Trim(),
            PartNumber = part, Rating = Component.Rating?.Trim(), Revision = revision,
            IsConfirmed = Component.IsConfirmed
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "models" });
    }

    public async Task<IActionResult> OnPostAddPositionAsync()
    {
        var models = await settings.GetMachineModelsAsync(HttpContext.RequestAborted);
        if (!models.Contains(Position.MachineModel, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Position.Key) || string.IsNullOrWhiteSpace(Position.Name))
            return await ErrorAsync("PositionRequired", "positions");
        var key = Position.Key.Trim();
        if (await db.MachinePositions.AnyAsync(x => x.MachineModel == Position.MachineModel && x.Key == key))
            return await ErrorAsync("PositionDuplicate", "positions");
        db.MachinePositions.Add(new MachinePosition
        {
            MachineModel = Position.MachineModel, Key = key, Name = Position.Name.Trim(),
            HmiLabel = Position.HmiLabel?.Trim(), SchemeLabel = Position.SchemeLabel?.Trim(),
            ComponentType = Position.ComponentType.Trim()
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        var position = await db.MachinePositions.FindAsync(Assignment.MachinePositionId);
        var component = await db.ComponentModels.FindAsync(Assignment.ComponentModelId);
        var machine = Assignment.MachineId is int id ? await db.Machines.FindAsync(id) : null;
        if (position == null || component == null || Assignment.MachineId != null && machine == null
            || machine != null && machine.Model != position!.MachineModel
            || Assignment.SerialFrom > Assignment.SerialTo
            || machine != null && (Assignment.SerialFrom != null || Assignment.SerialTo != null)
            || (Assignment.SerialFrom == null) != (Assignment.SerialTo == null))
            return await ErrorAsync("AssignmentInvalid", "configurations");

        var existing = await db.ComponentInstallations.Where(x => x.MachinePositionId == position!.Id
            && x.MachineId == Assignment.MachineId).ToListAsync();
        if (existing.Any(x => machine != null || x.SerialFrom == null && Assignment.SerialFrom == null
            || x.SerialFrom != null && Assignment.SerialFrom != null
                && x.SerialFrom <= Assignment.SerialTo && Assignment.SerialFrom <= x.SerialTo))
            return await ErrorAsync("AssignmentConflict", "configurations");
        db.ComponentInstallations.Add(new ComponentInstallation
        {
            MachinePositionId = position.Id, ComponentModelId = component.Id, MachineId = machine?.Id,
            SerialFrom = Assignment.SerialFrom, SerialTo = Assignment.SerialTo,
            IsConfirmed = Assignment.IsConfirmed
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "configurations" });
    }

    private async Task<IActionResult> ErrorAsync(string key, string tab)
    {
        ModelState.AddModelError(string.Empty, UiText.T(HttpContext, key));
        await LoadAsync(tab);
        return Page();
    }

    private async Task LoadAsync(string tab)
    {
        Tab = tab is "positions" or "configurations" ? tab : "models";
        Components = await db.ComponentModels.AsNoTracking().OrderBy(x => x.Manufacturer).ThenBy(x => x.PartNumber).ToListAsync();
        Positions = await db.MachinePositions.AsNoTracking().OrderBy(x => x.MachineModel).ThenBy(x => x.Name).ToListAsync();
        Installations = await db.ComponentInstallations.AsNoTracking().Include(x => x.MachinePosition)
            .Include(x => x.ComponentModel).Include(x => x.Machine).OrderBy(x => x.MachinePosition.MachineModel)
            .ThenBy(x => x.MachinePosition.Name).ToListAsync();
        Machines = await db.Machines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        MachineModels = await settings.GetMachineModelsAsync(HttpContext.RequestAborted);
    }

    public class ComponentInput
    {
        public string Type { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Series { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string? Rating { get; set; }
        public string? Revision { get; set; }
        public bool IsConfirmed { get; set; }
    }
    public class PositionInput
    {
        public string MachineModel { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? HmiLabel { get; set; }
        public string? SchemeLabel { get; set; }
        public string ComponentType { get; set; } = "";
    }
    public class AssignmentInput
    {
        public int MachinePositionId { get; set; }
        public int ComponentModelId { get; set; }
        public int? MachineId { get; set; }
        public int? SerialFrom { get; set; }
        public int? SerialTo { get; set; }
        public bool IsConfirmed { get; set; }
    }
}
