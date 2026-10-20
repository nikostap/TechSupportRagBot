using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

        _db.Machines.Add(new Machine
        {
            Name = Input.Name.Trim(),
            Model = Input.Model.Trim(),
            SerialNumber = SerialNumberFormatter.NormalizeMachine(Input.SerialNumber),
            Description = Input.Description,
            LicenseKey = LicenseKeyGenerator.Generate()
        });

        await _db.SaveChangesAsync();
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

        machine.Name = input.Name.Trim();
        machine.Model = input.Model.Trim();
        machine.SerialNumber = SerialNumberFormatter.NormalizeMachine(input.SerialNumber);
        machine.Description = input.Description;
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var machine = await _db.Machines.FindAsync(id);

        if (machine != null)
        {
            var hasTickets = await _db.Tickets.AnyAsync(x => x.MachineId == id);
            var hasKnowledge = await _db.KnowledgeDocuments.AnyAsync(x => x.MachineId == id);
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
