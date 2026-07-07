using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Areas.Identity.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SmtpEmailSender _emailSender;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, SmtpEmailSender emailSender)
    {
        _userManager = userManager;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var loginOrEmail = Input.LoginOrEmail.Trim();
        var user = await _userManager.FindByEmailAsync(loginOrEmail)
            ?? await _userManager.FindByNameAsync(loginOrEmail);

        // Не раскрываем, существует ли пользователь. Если почта указана, отправляем ссылку.
        if (user?.Email != null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var url = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code },
                protocol: Request.Scheme);

            await _emailSender.SendAsync(
                user.Email,
                "Class-Engineering Support: password reset",
                $"<p>Open this link to reset your password:</p><p><a href=\"{url}\">{url}</a></p>",
                HttpContext.RequestAborted);
        }

        Sent = true;
        return Page();
    }

    public class InputModel
    {
        [Required]
        public string LoginOrEmail { get; set; } = string.Empty;
    }
}
