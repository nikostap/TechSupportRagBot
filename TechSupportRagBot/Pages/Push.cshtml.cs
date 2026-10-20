using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages;

[Authorize]
public sealed class PushModel(BrowserPushService push) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Profile");

    public async Task<IActionResult> OnPostSubscribeAsync(string endpoint, string p256dh, string auth)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return new JsonResult(new { ok = await push.SubscribeAsync(userId, endpoint, p256dh, auth, HttpContext.RequestAborted) });
    }

    public async Task<IActionResult> OnPostUnsubscribeAsync(string endpoint)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await push.UnsubscribeAsync(userId, endpoint, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true });
    }
}
