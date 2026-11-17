using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class MachinesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly SystemSettingsService _settings;

    public MachinesModel(ApplicationDbContext db, SystemSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    [BindProperty]
    public MachineInput Input { get; set; } = new();

    [TempData]
    public string? AlertMessage { get; set; }

    public List<Machine> Machines { get; private set; } = new();
    public IReadOnlyList<string> MachineModelOptions { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!MachineModelOptions.Contains(Input.Model, StringComparer.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(Input.Model), UiText.T(HttpContext, "SelectMachineModel"));
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var model = MachineModelOptions.First(x => string.Equals(x, Input.Model, StringComparison.OrdinalIgnoreCase));
        var serial = SerialNumberFormatter.NormalizeMachine(Input.SerialNumber);
        if (await SerialAlreadyUsedAsync(model, serial))
        {
            ModelState.AddModelError(nameof(Input.SerialNumber), UiText.T(HttpContext, "MachineSerialDuplicate"));
            return Page();
        }

        _db.Machines.Add(new Machine
        {
            Name = Input.Name.Trim(),
            Model = model,
            SerialNumber = serial,
            Description = Input.Description,
            LicenseKey = LicenseKeyGenerator.Generate()
        });

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsDuplicateMachine(ex))
        {
            ModelState.AddModelError(nameof(Input.SerialNumber), UiText.T(HttpContext, "MachineSerialDuplicate"));
            return Page();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, MachineInput input)
    {
        var machine = await _db.Machines.FindAsync(id);
        if (machine == null)
        {
            return RedirectToPage();
        }

        await LoadAsync();
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.SerialNumber)
            || (!MachineModelOptions.Contains(input.Model, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(input.Model, machine.Model, StringComparison.OrdinalIgnoreCase)))
        {
            AlertMessage = UiText.T(HttpContext, "MachineModelInvalidSelection");
            return RedirectToPage();
        }

        var model = MachineModelOptions.FirstOrDefault(x => string.Equals(x, input.Model, StringComparison.OrdinalIgnoreCase))
            ?? machine.Model;
        var serial = SerialNumberFormatter.NormalizeMachine(input.SerialNumber);
        if (await SerialAlreadyUsedAsync(model, serial, id))
        {
            AlertMessage = UiText.T(HttpContext, "MachineSerialDuplicate");
            return RedirectToPage();
        }

        machine.Name = input.Name.Trim();
        machine.Model = model;
        machine.SerialNumber = serial;
        machine.Description = input.Description;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsDuplicateMachine(ex))
        {
            AlertMessage = UiText.T(HttpContext, "MachineSerialDuplicate");
            return RedirectToPage();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var machine = await _db.Machines.FindAsync(id);

        if (machine != null)
        {
            var hasTickets = await _db.Tickets.AnyAsync(x => x.MachineId == id);
            var hasKnowledge = await _db.KnowledgeDocuments.AnyAsync(x => x.MachineId == id)
                || await _db.KnowledgeDocumentTargets.AnyAsync(x => x.MachineId == id);
            if (hasTickets || hasKnowledge)
            {
                AlertMessage = TechSupportRagBot.Services.UiText.T(HttpContext, "MachineDeleteBlocked");
                return RedirectToPage();
            }

            var licenses = await _db.Licenses.Where(x => x.MachineId == id).ToListAsync();
            var clientMachines = await _db.ClientMachines.Where(x => x.MachineId == id).ToListAsync();
            _db.Licenses.RemoveRange(licenses);
            _db.ClientMachines.RemoveRange(clientMachines);
            _db.Machines.Remove(machine);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Machines = await _db.Machines.OrderBy(x => x.Name).ToListAsync();
        MachineModelOptions = await _settings.GetMachineModelsAsync(HttpContext.RequestAborted);
    }

    private async Task<bool> SerialAlreadyUsedAsync(string model, string serial, int? exceptId = null)
    {
        var existing = await _db.Machines.AsNoTracking()
            .Where(x => x.Model == model && (!exceptId.HasValue || x.Id != exceptId.Value))
            .Select(x => x.SerialNumber).ToListAsync();
        return existing.Any(x => SerialNumberFormatter.NormalizeMachine(x) == serial);
    }

    private static bool IsDuplicateMachine(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Machines_Model_SerialNumber"
        };

    public class MachineInput
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Model { get; set; } = string.Empty;

        [Required]
        public string SerialNumber { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}
