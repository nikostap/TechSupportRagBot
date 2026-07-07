using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class OperatorsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public OperatorsModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    [BindProperty]
    public OperatorInput Input { get; set; } = new();

    public IList<ApplicationUser> Operators { get; private set; } = new List<ApplicationUser>();
    public IReadOnlyList<(string Code, string Name)> LanguageOptions => ChatTranslationService.SupportedLanguages;
    public IReadOnlyList<(string Key, string Name)> AccessProfileOptions => AccessProfileService.ProfileOptions;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await _userManager.FindByNameAsync(Input.UserName.Trim()) != null)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
        }

        if (!ModelState.IsValid)
        {
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
            AccessProfile = AccessProfileService.NormalizeProfileKey(Input.AccessProfile),
            Gender = Input.Gender,
            Country = ChatTranslationService.NormalizeLanguage(Input.Country),
            IssuedPassword = temporaryPassword,
            MustChangePassword = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, temporaryPassword);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "Operator");
            return RedirectToPage();
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user != null && await _userManager.IsInRoleAsync(user, "Operator"))
        {
            await _userManager.DeleteAsync(user);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(string id, OperatorInput input)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Operator"))
        {
            return RedirectToPage();
        }

        var existing = await _userManager.FindByNameAsync(input.UserName.Trim());
        if (existing != null && existing.Id != user.Id)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
            await LoadAsync();
            return Page();
        }

        user.FullName = input.FullName.Trim();
        user.UserName = input.UserName.Trim();
        user.Email = input.Email;
        user.Position = input.Position;
        user.AccessProfile = AccessProfileService.NormalizeProfileKey(input.AccessProfile);
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
        if (user != null && await _userManager.IsInRoleAsync(user, "Operator") && !string.IsNullOrWhiteSpace(password))
        {
            await _userManager.RemovePasswordAsync(user);
            var result = await _userManager.AddPasswordAsync(user, password);
            if (result.Succeeded)
            {
                user.IssuedPassword = password;
                user.MustChangePassword = true;
                await _userManager.UpdateAsync(user);
            }
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Operators = await _userManager.GetUsersInRoleAsync("Operator");
    }

    public class OperatorInput
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
