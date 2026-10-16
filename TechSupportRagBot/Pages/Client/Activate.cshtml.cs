using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class ActivateModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ActivateModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public ActivateInput Input { get; set; } = new();

    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _db.Users.FirstAsync(x => x.Id == userId);
        var key = Input.Key.Trim().ToUpperInvariant();

        var license = await _db.Licenses
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Key == key && x.IsActive);

        if (license == null || license.ExpiresAt < DateTime.UtcNow)
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "LicenseKeyInvalid"));
            return Page();
        }

        if (license.IsActivated && license.ActivatedByUserId != userId)
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "LicenseAlreadyUsed"));
            return Page();
        }

        user.ClientId = license.ClientId;
        license.IsActivated = true;
        license.ActivatedByUserId = userId;
        license.ActivatedAt = DateTime.UtcNow;

        if (license.Machine != null)
        {
            license.Machine.IsLicenseActivated = true;
            license.Machine.ActivatedByUserId = userId;
            license.Machine.ActivatedAt = DateTime.UtcNow;
        }

        var hasAccess = await _db.ClientMachines.AnyAsync(x =>
            x.ClientId == license.ClientId &&
            x.MachineId == license.MachineId);

        if (!hasAccess)
        {
            _db.ClientMachines.Add(new Models.ClientMachine
            {
                ClientId = license.ClientId,
                MachineId = license.MachineId
            });
        }

        await _db.SaveChangesAsync();

        Message = TechSupportRagBot.Services.UiText.T(HttpContext, "LicenseActivatedMessage");
        return Page();
    }

    public class ActivateInput
    {
        [Required]
        public string Key { get; set; } = string.Empty;
    }
}
