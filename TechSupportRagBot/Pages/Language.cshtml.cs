using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TechSupportRagBot.Pages;

public class LanguageModel : PageModel
{
    public IActionResult OnGet(string lang = "ru", string? returnUrl = null)
    {
        var value = lang == "en" ? "en" : "ru";
        Response.Cookies.Append("lang", value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            SameSite = SameSiteMode.Lax
        });

        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }
}
