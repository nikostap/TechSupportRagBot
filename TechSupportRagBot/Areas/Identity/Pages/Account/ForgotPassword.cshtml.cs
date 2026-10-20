using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Areas.Identity.Pages.Account;

public class ForgotPasswordModel(PasswordRecoveryService recovery) : PageModel
{
    [BindProperty]
    public string? Email { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || !MailAddress.TryCreate(Email, out var address)
            || !string.Equals(address.Address, Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Email), UiText.T(HttpContext, "RecoveryEmailInvalid"));
            return Page();
        }

        await recovery.RequestAsync(HttpContext, Email);
        return RedirectToPage("./ResetPassword", new { email = Email.Trim() });
    }
}
