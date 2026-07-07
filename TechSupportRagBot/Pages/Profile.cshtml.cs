using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ProfileModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public string? CurrentAvatarPath { get; private set; }
    public string? FullName { get; private set; }
    public string? UserName { get; private set; }
    public string? Email { get; private set; }
    public string? Position { get; private set; }
    public string? Gender { get; private set; }
    public string? Language { get; private set; }
    public bool AutoTranslateMessages { get; private set; }
    public string Initials { get; private set; } = "CE";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        Fill(user);
        return Page();
    }

    private void Fill(ApplicationUser user)
    {
        CurrentAvatarPath = user.AvatarPath;
        FullName = user.FullName;
        UserName = user.UserName;
        Email = user.Email;
        Position = user.Position;
        Gender = user.Gender;
        Language = ChatTranslationService.NormalizeLanguage(user.Country);
        AutoTranslateMessages = user.AutoTranslateMessages;
        Initials = string.Join("", (user.FullName ?? user.UserName ?? "CE")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => x[0]))
            .ToUpperInvariant();
    }
}
