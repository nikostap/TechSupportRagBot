using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class AdminsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AccountDeactivationService _deactivation;

    public AdminsModel(UserManager<ApplicationUser> userManager, AccountDeactivationService deactivation)
    {
        _userManager = userManager;
        _deactivation = deactivation;
    }

    [BindProperty]
    public AdminInput Input { get; set; } = new();

    public IList<ApplicationUser> Admins { get; private set; } = new List<ApplicationUser>();
    public IReadOnlyList<(string Code, string Name)> LanguageOptions => ChatTranslationService.SupportedLanguages;
    public IReadOnlyList<(string Key, string Name)> AccessProfileOptions => AccessProfileService.GetProfileOptions(HttpContext, false);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Input ??= new AdminInput();
        if (string.IsNullOrWhiteSpace(Input.UserName))
        {
            ModelState.AddModelError(nameof(Input.UserName), TechSupportRagBot.Services.UiText.T(HttpContext, "UserNameRequired"));
        }
        if (string.IsNullOrWhiteSpace(Input.FullName))
        {
            ModelState.AddModelError(nameof(Input.FullName), TechSupportRagBot.Services.UiText.T(HttpContext, "FullNameRequired"));
        }
        var profile = AccessProfileService.NormalizeProfileKey(Input.AccessProfile);
        if (!AccessProfileOptions.Any(x => x.Key == profile))
            ModelState.AddModelError(nameof(Input.AccessProfile), UiText.T(HttpContext, "AccessProfileRequired"));

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        if (await _userManager.FindByNameAsync(Input.UserName.Trim()) != null)
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "LoginTaken"));
            await LoadAsync();
            return Page();
        }

        var temporaryPassword = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = Input.UserName.Trim(),
            Email = Input.Email,
            FullName = Input.FullName.Trim(),
            Position = Input.Position,
            AccessProfile = profile,
            Gender = Input.Gender,
            Country = ChatTranslationService.NormalizeLanguage(Input.Country),
            MustChangePassword = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, temporaryPassword);
        if (result.Succeeded)
        {
            var roleResult = await _userManager.AddToRoleAsync(user,
                profile == AccessProfileService.Administrator ? "Admin" : "Operator");
            if (roleResult.Succeeded)
            {
                TempData["IssuedPasswordOnce"] = temporaryPassword;
                TempData["IssuedUserOnce"] = user.UserName;
                return RedirectToPage();
            }
            await _userManager.DeleteAsync(user);
            result = roleResult;
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(string id, AdminInput input)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || await _userManager.IsInRoleAsync(user, "Client"))
        {
            return RedirectToPage();
        }

        var profile = AccessProfileService.NormalizeProfileKey(input.AccessProfile);
        if (!AccessProfileOptions.Any(x => x.Key == profile))
        {
            ModelState.AddModelError(nameof(input.AccessProfile), UiText.T(HttpContext, "AccessProfileRequired"));
            await LoadAsync();
            return Page();
        }

        var existing = await _userManager.FindByNameAsync(input.UserName.Trim());
        if (existing != null && existing.Id != user.Id)
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "LoginTaken"));
            await LoadAsync();
            return Page();
        }

        user.FullName = input.FullName.Trim();
        user.UserName = input.UserName.Trim();
        user.Email = input.Email;
        user.Position = input.Position;
        user.AccessProfile = profile;
        user.Gender = input.Gender;
        user.Country = ChatTranslationService.NormalizeLanguage(input.Country);
        user.NormalizedUserName = _userManager.NormalizeName(user.UserName);
        user.NormalizedEmail = _userManager.NormalizeEmail(user.Email);
        await _userManager.UpdateAsync(user);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(string id, string password)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user != null && !await _userManager.IsInRoleAsync(user, "Client") && !string.IsNullOrWhiteSpace(password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (result.Succeeded)
            {
                user.MustChangePassword = true;
                await _userManager.UpdateAsync(user);
                TempData["IssuedPasswordOnce"] = password;
                TempData["IssuedUserOnce"] = user.UserName;
            }
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var admins = (await _userManager.GetUsersInRoleAsync("Admin"))
            .Where(x => !AccountDeactivationService.IsDisabled(x)).ToList();
        var user = await _userManager.FindByIdAsync(id);
        if (user != null && !await _userManager.IsInRoleAsync(user, "Client")
            && (!await _userManager.IsInRoleAsync(user, "Admin") || admins.Count > 1))
        {
            await _deactivation.DeactivateAsync(user);
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var clients = await _userManager.GetUsersInRoleAsync("Client");
        var clientIds = clients.Select(x => x.Id).ToHashSet();
        Admins = await _userManager.Users
            .AsNoTracking()
            .Where(x => !clientIds.Contains(x.Id) && x.LockoutEnd != AccountDeactivationService.DisabledUntil)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.UserName)
            .ToListAsync();
    }

    public class AdminInput
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Position { get; set; }

        public string? AccessProfile { get; set; } = AccessProfileService.Operator;

        public string? Gender { get; set; }

        public string? Country { get; set; } = "ru";
    }
}
