using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages;

[Authorize]
public class ProfileEditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FileStorageService _storage;
    private readonly FileUploadValidationService _uploads;

    public ProfileEditModel(
        UserManager<ApplicationUser> userManager,
        FileStorageService storage,
        FileUploadValidationService uploads)
    {
        _userManager = userManager;
        _storage = storage;
        _uploads = uploads;
    }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    [BindProperty]
    public ChangePasswordInput PasswordInput { get; set; } = new();

    public string? CurrentAvatarPath { get; private set; }
    public string? FullName { get; private set; }
    public string? UserName { get; private set; }
    public string Initials { get; private set; } = "CE";
    public IReadOnlyList<(string Code, string Name)> LanguageOptions => ChatTranslationService.SupportedLanguages;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        Fill(user);
        Input.FullName = user.FullName;
        Input.Position = user.Position;
        Input.Gender = user.Gender;
        Input.Language = ChatTranslationService.LanguageToLibreTranslateCode(user.Country) ?? "ru";
        Input.AutoTranslateMessages = user.AutoTranslateMessages;
        Input.WorkdayStart = MinutesToTime(user.WorkdayStartMinutes);
        Input.WorkdayEnd = MinutesToTime(user.WorkdayEndMinutes);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        user.FullName = Input.FullName;
        user.Position = Input.Position;
        user.Gender = Input.Gender;
        user.Country = ChatTranslationService.NormalizeLanguage(Input.Language);
        user.AutoTranslateMessages = Input.AutoTranslateMessages;
        user.WorkdayStartMinutes = TimeToMinutes(Input.WorkdayStart, 8 * 60);
        user.WorkdayEndMinutes = TimeToMinutes(Input.WorkdayEnd, 17 * 60);

        string? previousAvatarPath = null;
        string? previousAvatarProvider = null;
        if (Input.Avatar is { Length: > 0 })
        {
            try
            {
                await using var upload = await _uploads.ValidateAsync(
                    Input.Avatar,
                    UploadPurpose.Avatar,
                    HttpContext.RequestAborted);
                var publicId = Guid.NewGuid();
                var provider = await _storage.GetSelectedProviderAsync(HttpContext.RequestAborted);
                var key = $"avatars/{user.Id}/{publicId:N}{upload.Extension}";
                await _storage.SaveAsync(
                    provider,
                    key,
                    upload.Content,
                    upload.ContentType,
                    HttpContext.RequestAborted);

                previousAvatarPath = user.AvatarPath;
                previousAvatarProvider = user.AvatarStorageProvider;
                user.AvatarPath = key;
                user.AvatarPublicId = publicId;
                user.AvatarStorageProvider = provider;
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                Fill(user);
                return Page();
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            Fill(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(previousAvatarPath))
        {
            await _storage.DeleteAsync(previousAvatarProvider, previousAvatarPath, HttpContext.RequestAborted);
        }

        return RedirectToPage("/Profile");
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (string.IsNullOrWhiteSpace(PasswordInput.CurrentPassword))
        {
            ModelState.AddModelError(nameof(PasswordInput.CurrentPassword), "Введите текущий пароль.");
        }
        if (string.IsNullOrWhiteSpace(PasswordInput.NewPassword) || PasswordInput.NewPassword.Length < 6)
        {
            ModelState.AddModelError(nameof(PasswordInput.NewPassword), "Новый пароль должен содержать не менее 6 символов.");
        }
        if (PasswordInput.NewPassword != PasswordInput.ConfirmPassword)
        {
            ModelState.AddModelError(nameof(PasswordInput.ConfirmPassword), "Пароли не совпадают.");
        }
        if (!ModelState.IsValid)
        {
            Fill(user);
            return Page();
        }

        var result = await _userManager.ChangePasswordAsync(user, PasswordInput.CurrentPassword, PasswordInput.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            Fill(user);
            return Page();
        }

        user.MustChangePassword = false;
        user.IssuedPassword = null;
        await _userManager.UpdateAsync(user);
        return RedirectToPage("/Profile");
    }

    private void Fill(ApplicationUser user)
    {
        CurrentAvatarPath = user.AvatarPublicId.HasValue
            ? $"attachments/{user.AvatarPublicId.Value:D}"
            : null;
        FullName = user.FullName;
        UserName = user.UserName;
        Initials = string.Join("", (user.FullName ?? user.UserName ?? "CE")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => x[0]))
            .ToUpperInvariant();
    }

    private static string MinutesToTime(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 23 * 60 + 59);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private static int TimeToMinutes(string? value, int fallback)
    {
        return TimeOnly.TryParse(value, out var time)
            ? time.Hour * 60 + time.Minute
            : fallback;
    }

    public class ProfileInput
    {
        [StringLength(100)]
        public string? FullName { get; set; }

        public string? Position { get; set; }

        public string? Gender { get; set; }

        public string? Language { get; set; } = "ru";

        public bool AutoTranslateMessages { get; set; } = true;

        public string? WorkdayStart { get; set; }

        public string? WorkdayEnd { get; set; }

        public IFormFile? Avatar { get; set; }
    }

    public class ChangePasswordInput
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
