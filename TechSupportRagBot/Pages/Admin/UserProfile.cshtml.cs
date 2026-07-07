using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UserProfileModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public UserProfileModel(UserManager<ApplicationUser> userManager, ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public UserProfileInput Input { get; set; } = new();

    public ApplicationUser? EditedUser { get; private set; }
    public IList<string> Roles { get; private set; } = new List<string>();
    public string Initials { get; private set; } = "CE";
    public IReadOnlyList<(string Code, string Name)> LanguageOptions => ChatTranslationService.SupportedLanguages;
    public IReadOnlyList<(string Key, string Name)> AccessProfileOptions => AccessProfileService.GetProfileOptions(HttpContext);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        FillInput();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existing = await _userManager.FindByNameAsync(Input.UserName.Trim());
        if (existing != null && existing.Id != EditedUser!.Id)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
            return Page();
        }

        EditedUser!.FullName = Input.FullName?.Trim();
        EditedUser.UserName = Input.UserName.Trim();
        EditedUser.Email = Input.Email;
        EditedUser.Position = Input.Position;
        EditedUser.AccessProfile = AccessProfileService.NormalizeProfileKey(Input.AccessProfile);
        EditedUser.Gender = Input.Gender;
        EditedUser.Country = ChatTranslationService.NormalizeLanguage(Input.Country);
        EditedUser.NormalizedUserName = _userManager.NormalizeName(EditedUser.UserName);
        EditedUser.NormalizedEmail = _userManager.NormalizeEmail(EditedUser.Email);

        if (EditedUser.ClientId.HasValue)
        {
            var client = await _db.Clients.FindAsync(EditedUser.ClientId.Value);
            if (client != null)
            {
                client.Name = Input.CompanyName?.Trim() ?? client.Name;
                client.ContactEmail = Input.Email;
                client.ContactPhone = Input.Phone;
            }
        }

        await _db.SaveChangesAsync();
        await _userManager.UpdateAsync(EditedUser);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(string password)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Укажите новый пароль.");
            FillInput();
            return Page();
        }

        await _userManager.RemovePasswordAsync(EditedUser!);
        var result = await _userManager.AddPasswordAsync(EditedUser!, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            FillInput();
            return Page();
        }

        EditedUser!.IssuedPassword = password;
        EditedUser.MustChangePassword = Roles.Contains("Operator") || Roles.Contains("Admin");
        await _userManager.UpdateAsync(EditedUser);
        return RedirectToPage(new { id = Id });
    }

    private async Task<bool> LoadAsync()
    {
        EditedUser = await _db.Users
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == Id);

        if (EditedUser == null)
        {
            return false;
        }

        Roles = await _userManager.GetRolesAsync(EditedUser);
        Initials = string.Join("", (EditedUser.FullName ?? EditedUser.UserName ?? "CE")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => x[0]))
            .ToUpperInvariant();

        return true;
    }

    private void FillInput()
    {
        Input.FullName = EditedUser?.FullName;
        Input.UserName = EditedUser?.UserName ?? string.Empty;
        Input.Email = EditedUser?.Email;
        Input.Position = EditedUser?.Position;
        Input.AccessProfile = AccessProfileService.NormalizeProfileKey(EditedUser?.AccessProfile);
        Input.Gender = EditedUser?.Gender;
        Input.Country = ChatTranslationService.LanguageToLibreTranslateCode(EditedUser?.Country) ?? "ru";
        Input.CompanyName = EditedUser?.Client?.Name;
        Input.Phone = EditedUser?.Client?.ContactPhone;
    }

    public class UserProfileInput
    {
        [StringLength(100)]
        public string? FullName { get; set; }

        [Required]
        public string UserName { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Position { get; set; }

        public string? AccessProfile { get; set; }

        public string? Gender { get; set; }

        public string? Country { get; set; }

        public string? CompanyName { get; set; }

        public string? Phone { get; set; }
    }
}
