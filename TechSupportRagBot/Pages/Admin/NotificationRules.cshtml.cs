using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public sealed class NotificationRulesModel(EmailNotificationRulesService rules) : PageModel
{
    public Dictionary<string, List<string>> Selected { get; private set; } = new();
    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync() => Selected = await rules.GetAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostAsync()
    {
        var selected = new Dictionary<string, List<string>>();
        foreach (var notification in EmailNotificationRulesService.EditableEvents)
        {
            selected[notification.Key] = EmailNotificationRulesService.InternalProfiles
                .Where(profile => Request.Form.ContainsKey($"rule_{notification.Key}_{profile.Key}"))
                .Select(profile => profile.Key)
                .ToList();
        }

        await rules.SaveAsync(selected, HttpContext.RequestAborted);
        Selected = await rules.GetAsync(HttpContext.RequestAborted);
        StatusMessage = UiText.T(HttpContext, "NotificationRulesSaved");
        return Page();
    }
}
