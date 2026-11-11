using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class ComponentsModel(ApplicationDbContext db, SystemSettingsService settings, QAService qaService) : PageModel
{
    [BindProperty] public ComponentInput Component { get; set; } = new();
    [BindProperty] public PositionInput Position { get; set; } = new();
    [BindProperty] public TemplateInput Template { get; set; } = new();
    [BindProperty] public AssignmentInput Assignment { get; set; } = new();
    [BindProperty] public ComponentInput EditComponent { get; set; } = new();
    [BindProperty] public PositionInput EditPosition { get; set; } = new();
    [BindProperty] public TemplateInput EditTemplate { get; set; } = new();
    [BindProperty] public AssignmentInput EditAssignment { get; set; } = new();
    [TempData] public string? Notice { get; set; }
    public string Tab { get; private set; } = "models";
    public List<ComponentModel> Components { get; private set; } = [];
    public List<MachinePosition> Positions { get; private set; } = [];
    public List<PositionTemplate> Templates { get; private set; } = [];
    public List<ComponentInstallation> Installations { get; private set; } = [];
    public List<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<string> MachineModels { get; private set; } = [];

    public async Task OnGetAsync(string tab = "models") => await LoadAsync(tab);

    public async Task<IActionResult> OnPostAddComponentAsync()
    {
        if (string.IsNullOrWhiteSpace(Component.Type) || string.IsNullOrWhiteSpace(Component.Manufacturer))
            return await ErrorAsync("ComponentRequired", "models");
        var type = Component.Type.Trim();
        var manufacturer = Component.Manufacturer.Trim();
        var series = Component.Series?.Trim() ?? "";
        var part = Component.PartNumber?.Trim() ?? "";
        var rating = Component.Rating?.Trim();
        if (string.IsNullOrWhiteSpace(series) && string.IsNullOrWhiteSpace(part))
            return await ErrorAsync("ComponentIdentifierRequired", "models");
        if (await db.ComponentModels.AnyAsync(x => x.Type.ToLower() == type.ToLower()
            && x.Manufacturer.ToLower() == manufacturer.ToLower()
            && x.Series.ToLower() == series.ToLower()
            && x.PartNumber.ToLower() == part.ToLower()
            && (x.Rating ?? "").ToLower() == (rating ?? "").ToLower()))
            return await ErrorAsync("ComponentDuplicate", "models");
        db.ComponentModels.Add(new ComponentModel
        {
            Type = type, Manufacturer = manufacturer, Series = series,
            PartNumber = part, Rating = rating,
            IsConfirmed = Component.IsConfirmed
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "models" });
    }

    public async Task<IActionResult> OnPostUpdateComponentAsync(int id)
    {
        var item = await db.ComponentModels.FindAsync(id);
        if (item == null) return NotFound();
        var type = EditComponent.Type?.Trim();
        var manufacturer = EditComponent.Manufacturer?.Trim();
        var series = EditComponent.Series?.Trim() ?? "";
        var part = EditComponent.PartNumber?.Trim() ?? "";
        var rating = EditComponent.Rating?.Trim();
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(manufacturer))
            return await ErrorAsync("ComponentRequired", "models");
        if (series.Length == 0 && part.Length == 0)
            return await ErrorAsync("ComponentIdentifierRequired", "models");
        if (await db.ComponentModels.AnyAsync(x => x.Id != id && x.Type.ToLower() == type.ToLower()
            && x.Manufacturer.ToLower() == manufacturer.ToLower() && x.Series.ToLower() == series.ToLower()
            && x.PartNumber.ToLower() == part.ToLower()
            && (x.Rating ?? "").ToLower() == (rating ?? "").ToLower()))
            return await ErrorAsync("ComponentDuplicate", "models");
        item.Type = type; item.Manufacturer = manufacturer; item.Series = series;
        item.PartNumber = part; item.Rating = rating; item.IsConfirmed = EditComponent.IsConfirmed;
        await db.SaveChangesAsync();
        var qaIds = await db.QAEntries.AsNoTracking().Where(x => x.ComponentModelId == id)
            .Select(x => x.Id).ToListAsync();
        foreach (var qaId in qaIds) await qaService.ReindexAsync(qaId, HttpContext.RequestAborted);
        return RedirectToPage(new { tab = "models" });
    }

    public async Task<IActionResult> OnPostArchiveComponentAsync(int id)
    {
        var item = await db.ComponentModels.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = true;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "models" });
    }

    public async Task<IActionResult> OnPostRestoreComponentAsync(int id)
    {
        var item = await db.ComponentModels.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = false;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "models" });
    }

    public async Task<IActionResult> OnPostAddPositionAsync()
    {
        var template = Position.PositionTemplateId is int templateId
            ? await db.PositionTemplates.FindAsync(templateId) : null;
        var name = string.IsNullOrWhiteSpace(Position.Name) ? template?.Name : Position.Name.Trim();
        var componentType = string.IsNullOrWhiteSpace(Position.ComponentType)
            ? template?.ComponentType : Position.ComponentType.Trim();
        var models = (await settings.GetMachineModelsAsync(HttpContext.RequestAborted))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync());
        if (!models.Contains(Position.MachineModel, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Position.Key) || string.IsNullOrWhiteSpace(name)
            || Position.PositionTemplateId != null && (template == null || template.IsArchived))
            return await ErrorAsync("PositionRequired", "positions");
        var key = Position.Key.Trim();
        if (await db.MachinePositions.AnyAsync(x => x.MachineModel == Position.MachineModel && x.Key == key))
            return await ErrorAsync("PositionDuplicate", "positions");
        db.MachinePositions.Add(new MachinePosition
        {
            MachineModel = Position.MachineModel, Key = key, Name = name,
            HmiLabel = Position.HmiLabel?.Trim(), SchemeLabel = Position.SchemeLabel?.Trim(),
            ComponentType = componentType ?? "", PositionTemplateId = template?.Id
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostUpdatePositionAsync(int id)
    {
        var item = await db.MachinePositions.FindAsync(id);
        if (item == null) return NotFound();
        var model = EditPosition.MachineModel?.Trim();
        var key = EditPosition.Key?.Trim();
        var name = EditPosition.Name?.Trim();
        var models = (await settings.GetMachineModelsAsync(HttpContext.RequestAborted))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync());
        if (string.IsNullOrWhiteSpace(model) || !models.Contains(model, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            return await ErrorAsync("PositionRequired", "positions");
        if (model != item.MachineModel && await db.ComponentInstallations.AnyAsync(x => x.MachinePositionId == id))
            return await ErrorAsync("PositionModelInUse", "positions");
        if (await db.MachinePositions.AnyAsync(x => x.Id != id && x.MachineModel == model && x.Key == key))
            return await ErrorAsync("PositionDuplicate", "positions");
        item.MachineModel = model; item.Key = key; item.Name = name;
        item.ComponentType = EditPosition.ComponentType?.Trim() ?? "";
        item.HmiLabel = EditPosition.HmiLabel?.Trim(); item.SchemeLabel = EditPosition.SchemeLabel?.Trim();
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostArchivePositionAsync(int id)
    {
        var item = await db.MachinePositions.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = true;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostRestorePositionAsync(int id)
    {
        var item = await db.MachinePositions.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = false;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostAddTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(Template.Name) || string.IsNullOrWhiteSpace(Template.ComponentType))
            return await ErrorAsync("TemplateRequired", "positions");
        var name = Template.Name.Trim();
        var type = Template.ComponentType.Trim();
        if (await db.PositionTemplates.AnyAsync(x => x.Name == name && x.ComponentType == type))
            return await ErrorAsync("TemplateDuplicate", "positions");
        db.PositionTemplates.Add(new PositionTemplate { Name = name, ComponentType = type });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostUpdateTemplateAsync(int id)
    {
        var item = await db.PositionTemplates.FindAsync(id);
        if (item == null) return NotFound();
        var name = EditTemplate.Name?.Trim();
        var type = EditTemplate.ComponentType?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
            return await ErrorAsync("TemplateRequired", "positions");
        if (await db.PositionTemplates.AnyAsync(x => x.Id != id && x.Name == name && x.ComponentType == type))
            return await ErrorAsync("TemplateDuplicate", "positions");
        item.Name = name; item.ComponentType = type;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostArchiveTemplateAsync(int id)
    {
        var item = await db.PositionTemplates.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = true;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostRestoreTemplateAsync(int id)
    {
        var item = await db.PositionTemplates.FindAsync(id);
        if (item == null) return NotFound();
        item.IsArchived = false;
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "positions" });
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        var position = await db.MachinePositions.FindAsync(Assignment.MachinePositionId);
        var component = await db.ComponentModels.FindAsync(Assignment.ComponentModelId);
        var models = (await settings.GetMachineModelsAsync(HttpContext.RequestAborted))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync());
        var specificSerial = string.IsNullOrWhiteSpace(Assignment.SpecificSerialNumber)
            ? null : Assignment.SpecificSerialNumber.Trim();
        if (position == null || position.IsArchived || component == null || component.IsArchived
            || !models.Contains(Assignment.MachineModel, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(Assignment.MachineModel, position.MachineModel, StringComparison.OrdinalIgnoreCase)
            || specificSerial?.Length > 100
            || Assignment.SerialFrom > Assignment.SerialTo
            || Assignment.SerialFrom is < 0 or > 999 || Assignment.SerialTo is < 0 or > 999
            || specificSerial != null && (Assignment.SerialFrom != null || Assignment.SerialTo != null)
            || (Assignment.SerialFrom == null) != (Assignment.SerialTo == null))
            return await ErrorAsync("AssignmentInvalid", "configurations");

        var legacyMachineIds = specificSerial == null ? new List<int>() : await db.Machines.AsNoTracking()
            .Where(x => x.Model == position.MachineModel && x.SerialNumber == specificSerial)
            .Select(x => x.Id).ToListAsync();
        var existing = await db.ComponentInstallations.Where(x => x.MachinePositionId == position.Id
            && x.IsActive).ToListAsync();
        var overlaps = existing.Where(x => specificSerial != null
                ? string.Equals(x.SpecificSerialNumber, specificSerial, StringComparison.OrdinalIgnoreCase)
                    || x.MachineId != null && legacyMachineIds.Contains(x.MachineId.Value)
                : x.MachineId == null && x.SpecificSerialNumber == null
                    && (Assignment.SerialFrom == null && x.SerialFrom == null
                        || Assignment.SerialFrom != null && x.SerialFrom != null
                            && x.SerialFrom <= Assignment.SerialTo && Assignment.SerialFrom <= x.SerialTo))
            .ToList();
        if (overlaps.Count > 0 && (!Assignment.ReplaceExisting
            || overlaps.Any(x => specificSerial == null
                && (x.SerialFrom != Assignment.SerialFrom || x.SerialTo != Assignment.SerialTo))))
            return await ErrorAsync("AssignmentConflict", "configurations");
        foreach (var old in overlaps)
        {
            old.IsActive = false;
            old.RetiredAt = DateTime.UtcNow;
        }
        db.ComponentInstallations.Add(new ComponentInstallation
        {
            MachinePositionId = position.Id, ComponentModelId = component.Id,
            SpecificSerialNumber = specificSerial,
            SerialFrom = Assignment.SerialFrom, SerialTo = Assignment.SerialTo,
            IsConfirmed = Assignment.IsConfirmed
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "configurations" });
    }

    public async Task<IActionResult> OnPostUpdateInstallationAsync(int id)
    {
        var current = await db.ComponentInstallations.Include(x => x.MachinePosition)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        var component = await db.ComponentModels.FindAsync(EditAssignment.ComponentModelId);
        if (current == null) return NotFound();
        var serial = string.IsNullOrWhiteSpace(EditAssignment.SpecificSerialNumber)
            ? null : EditAssignment.SpecificSerialNumber.Trim();
        if (component == null || component.IsArchived || current.MachinePosition.IsArchived
            || serial?.Length > 100 || EditAssignment.SerialFrom > EditAssignment.SerialTo
            || EditAssignment.SerialFrom is < 0 or > 999 || EditAssignment.SerialTo is < 0 or > 999
            || (EditAssignment.SerialFrom == null) != (EditAssignment.SerialTo == null)
            || serial != null && EditAssignment.SerialFrom != null
            || current.MachineId != null && (serial != null || EditAssignment.SerialFrom != null))
            return await ErrorAsync("AssignmentInvalid", "configurations");

        var other = await db.ComponentInstallations.AsNoTracking()
            .Where(x => x.MachinePositionId == current.MachinePositionId && x.IsActive && x.Id != id)
            .ToListAsync();
        if (other.Any(x => current.MachineId != null && x.MachineId == current.MachineId
            || current.MachineId == null && serial != null
                && string.Equals(x.SpecificSerialNumber, serial, StringComparison.OrdinalIgnoreCase)
            || current.MachineId == null && serial == null && x.MachineId == null && x.SpecificSerialNumber == null
                && (EditAssignment.SerialFrom == null && x.SerialFrom == null
                    || EditAssignment.SerialFrom != null && x.SerialFrom != null
                        && x.SerialFrom <= EditAssignment.SerialTo && EditAssignment.SerialFrom <= x.SerialTo)))
            return await ErrorAsync("AssignmentConflict", "configurations");

        current.IsActive = false;
        current.RetiredAt = DateTime.UtcNow;
        db.ComponentInstallations.Add(new ComponentInstallation
        {
            MachinePositionId = current.MachinePositionId, ComponentModelId = component.Id,
            MachineId = current.MachineId, SpecificSerialNumber = serial,
            SerialFrom = EditAssignment.SerialFrom, SerialTo = EditAssignment.SerialTo,
            IsConfirmed = EditAssignment.IsConfirmed
        });
        await db.SaveChangesAsync();
        return RedirectToPage(new { tab = "configurations" });
    }

    public async Task<IActionResult> OnPostArchiveInstallationAsync(int id)
    {
        var item = await db.ComponentInstallations.FindAsync(id);
        if (item == null) return NotFound();
        item.IsActive = false;
        item.RetiredAt = DateTime.UtcNow;
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
        Templates = await db.PositionTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        Installations = await db.ComponentInstallations.AsNoTracking().Include(x => x.MachinePosition)
            .Include(x => x.ComponentModel).Include(x => x.Machine).OrderBy(x => x.MachinePosition.MachineModel)
            .ThenBy(x => x.MachinePosition.Name).ThenByDescending(x => x.IsActive).ToListAsync();
        Machines = await db.Machines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        MachineModels = (await settings.GetMachineModelsAsync(HttpContext.RequestAborted))
            .Concat(Machines.Select(x => x.Model)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public class ComponentInput
    {
        public string Type { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string? Series { get; set; }
        public string? PartNumber { get; set; }
        public string? Rating { get; set; }
        public bool IsConfirmed { get; set; }
    }
    public class PositionInput
    {
        public int? PositionTemplateId { get; set; }
        public string MachineModel { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? HmiLabel { get; set; }
        public string? SchemeLabel { get; set; }
        public string ComponentType { get; set; } = "";
    }
    public class TemplateInput
    {
        public string Name { get; set; } = "";
        public string ComponentType { get; set; } = "";
    }
    public class AssignmentInput
    {
        public int MachinePositionId { get; set; }
        public int ComponentModelId { get; set; }
        public string MachineModel { get; set; } = "";
        public string? SpecificSerialNumber { get; set; }
        public int? SerialFrom { get; set; }
        public int? SerialTo { get; set; }
        public bool IsConfirmed { get; set; }
        public bool ReplaceExisting { get; set; }
    }
}
