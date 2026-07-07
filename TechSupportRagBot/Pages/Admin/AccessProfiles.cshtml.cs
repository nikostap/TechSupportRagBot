using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AccessProfilesModel : PageModel
{
    private readonly AccessProfileService _access;

    public AccessProfilesModel(AccessProfileService access)
    {
        _access = access;
    }

    public IReadOnlyList<AccessPermissionDefinition> Permissions => AccessProfileService.PermissionDefinitions;

    public List<AccessProfileRule> Profiles { get; private set; } = new();

    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Profiles = await _access.GetProfilesAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existing = await _access.GetProfilesAsync(HttpContext.RequestAborted);
        foreach (var profile in existing)
        {
            foreach (var permission in Permissions)
            {
                var key = $"perm_{profile.Key}_{permission.Key}";
                profile.Permissions[permission.Key] = Request.Form.ContainsKey(key);
            }
        }

        await _access.SaveProfilesAsync(existing, HttpContext.RequestAborted);
        Profiles = existing;
        StatusMessage = "Профили доступа сохранены.";
        return Page();
    }
}
