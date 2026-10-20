using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class LicensesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly LicenseMailService _mail;
    private readonly ILogger<LicensesModel> _logger;

    public LicensesModel(ApplicationDbContext db, LicenseMailService mail, ILogger<LicensesModel> logger)
    {
        _db = db;
        _mail = mail;
        _logger = logger;
    }

    [BindProperty]
    public LicenseInput Input { get; set; } = new();

    public List<License> Licenses { get; private set; } = new();
    public SelectList ClientOptions { get; private set; } = new(Array.Empty<object>());
    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        if (!await _db.Clients.AnyAsync(x => x.Id == Input.ClientId)
            || !await _db.Machines.AnyAsync(x => x.Id == Input.MachineId))
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "LicenseSelectionInvalid"));
            await LoadAsync();
            return Page();
        }

        var exists = await _db.Licenses.AnyAsync(x =>
            x.ClientId == Input.ClientId &&
            x.MachineId == Input.MachineId &&
            x.IsActive);

        if (exists)
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "LicenseAlreadyActive"));
            await LoadAsync();
            return Page();
        }

        var license = new License
        {
            ClientId = Input.ClientId,
            MachineId = Input.MachineId,
            Key = LicenseKeyGenerator.Generate(),
            ExpiresAt = Input.ExpiresAt.HasValue
                ? DateTime.SpecifyKind(Input.ExpiresAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : null
        };

        _db.Licenses.Add(license);
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendKeyAsync(int id)
    {
        try
        {
            var result = await _mail.SendKeyAsync(id, HttpContext.RequestAborted);
            var key = result switch
            {
                LicenseKeyMailResult.Sent => "LicenseMailSent",
                LicenseKeyMailResult.NoEmail => "LicenseMailNoEmail",
                _ => "LicenseMailUnavailable"
            };
            TempData["LicenseMailStatus"] = UiText.T(HttpContext, key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send license key for license {LicenseId}.", id);
            TempData["LicenseMailStatus"] = UiText.T(HttpContext, "LicenseMailFailed");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var license = await _db.Licenses.FindAsync(id);
        if (license != null)
        {
            _db.Licenses.Remove(license);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Licenses = await _db.Licenses
            .Include(x => x.Client)
            .Include(x => x.Machine)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        ClientOptions = new SelectList(await _db.Clients.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        MachineOptions = new SelectList(await _db.Machines.OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
    }

    public class LicenseInput
    {
        [Required]
        public int ClientId { get; set; }

        [Required]
        public int MachineId { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }
}
