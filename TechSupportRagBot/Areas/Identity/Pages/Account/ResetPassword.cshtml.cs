using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Areas.Identity.Pages.Account;

public class ResetPasswordModel(
    UserManager<ApplicationUser> users,
    PasswordRecoveryService recovery) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CodeVerified { get; private set; }
    public bool PasswordChanged { get; private set; }

    public void OnGet(string? email) => Input.Email = email?.Trim() ?? string.Empty;

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        if (!ValidateEmailAndCode()) return Page();
        CodeVerified = await recovery.VerifyAsync(Input.Email!, Input.Code!) != null;
        if (!CodeVerified) ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "RecoveryCodeInvalid"));
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        CodeVerified = true;
        if (!ValidateEmailAndCode()) return Page();
        if (string.IsNullOrWhiteSpace(Input.Password) || Input.Password.Length < 6)
            ModelState.AddModelError(nameof(Input.Password), UiText.T(HttpContext, "NewPasswordMinLength"));
        if (Input.Password != Input.ConfirmPassword)
            ModelState.AddModelError(nameof(Input.ConfirmPassword), UiText.T(HttpContext, "PasswordsDoNotMatch"));
        if (!ModelState.IsValid) return Page();

        var user = await recovery.VerifyAsync(Input.Email!, Input.Code!);
        if (user == null)
        {
            CodeVerified = false;
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "RecoveryCodeInvalid"));
            return Page();
        }

        user.MustChangePassword = false;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, Input.Password!);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "RecoveryPasswordRejected"));
            return Page();
        }

        await recovery.ConsumeAsync(user);
        PasswordChanged = true;
        CodeVerified = false;
        return Page();
    }

    private bool ValidateEmailAndCode()
    {
        if (string.IsNullOrWhiteSpace(Input.Email) || !MailAddress.TryCreate(Input.Email, out var address)
            || !string.Equals(address.Address, Input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(Input.Email), UiText.T(HttpContext, "RecoveryEmailInvalid"));
        if (Input.Code is not { Length: 6 } || Input.Code.Any(c => c is < '0' or > '9'))
            ModelState.AddModelError(nameof(Input.Code), UiText.T(HttpContext, "RecoveryCodeInvalid"));
        return ModelState.IsValid;
    }

    public class InputModel
    {
        public string? Email { get; set; }

        public string? Code { get; set; }

        public string? Password { get; set; }

        public string? ConfirmPassword { get; set; }
    }
}
