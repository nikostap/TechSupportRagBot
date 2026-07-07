using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class MachinesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public MachinesModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public MachineInput Input { get; set; } = new();

    [TempData]
    public string? AlertMessage { get; set; }

    public List<Machine> Machines { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.Machines.Add(new Machine
        {
            Name = Input.Name.Trim(),
            Model = Input.Model.Trim(),
            SerialNumber = Input.SerialNumber.Trim(),
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

        machine.Name = input.Name.Trim();
        machine.Model = input.Model.Trim();
        machine.SerialNumber = input.SerialNumber.Trim();
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
                AlertMessage = "Станок нельзя удалить, пока к нему привязаны обращения или документы базы знаний.";
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
