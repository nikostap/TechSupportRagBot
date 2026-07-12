using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class UsersModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TicketDeletionService _ticketDeletion;

    public UsersModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TicketDeletionService ticketDeletion)
    {
        _db = db;
        _userManager = userManager;
        _ticketDeletion = ticketDeletion;
    }

    [BindProperty]
    public CompanyUserInput Input { get; set; } = new();

    public List<ApplicationUser> Users { get; private set; } = new();
    public string? CurrentUserId { get; private set; }
    public IReadOnlyList<(string Code, string Name)> LanguageOptions => ChatTranslationService.SupportedLanguages;
    public IReadOnlyList<(string Key, string Name)> AccessProfileOptions => AccessProfileService.GetProfileOptions(HttpContext)
        .Where(x => x.Key is AccessProfileService.Manager or AccessProfileService.Engineer or AccessProfileService.Observer)
        .ToList();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var clientId = await GetClientIdAsync();
        if (clientId == null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var companyName = await _db.Clients
            .Where(x => x.Id == clientId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? "client";
        var userName = string.IsNullOrWhiteSpace(Input.UserName)
            ? await GenerateCompanyUserNameAsync(companyName, Input.FullName)
            : Input.UserName.Trim();

        if (await _userManager.FindByNameAsync(userName) != null)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
            await LoadAsync();
            return Page();
        }

        var password = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = Input.Email,
            FullName = Input.FullName.Trim(),
            Position = Input.Position,
            AccessProfile = AccessProfileService.NormalizeProfileKey(Input.AccessProfile),
            Country = ChatTranslationService.NormalizeLanguage(Input.Country),
            ClientId = clientId,
            IssuedPassword = password,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "Client");
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
        var clientId = await GetClientIdAsync();
        if (clientId == null)
        {
            return Forbid();
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && x.ClientId == clientId);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user != null && user.Id != currentUserId)
        {
            await _ticketDeletion.DeleteTicketsForUsersAsync(new[] { user.Id });
            await _userManager.DeleteAsync(user);
        }

        return RedirectToPage();
    }

    private async Task<bool> LoadAsync()
    {
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientId = await GetClientIdAsync();
        if (clientId == null)
        {
            return false;
        }

        Users = await _db.Users
            .Where(x => x.ClientId == clientId)
            .OrderBy(x => x.FullName ?? x.UserName)
            .ToListAsync();

        return true;
    }

    private async Task<int?> GetClientIdAsync()
    {
        if (User.IsInRole("Admin"))
        {
            return await _db.Clients.OrderBy(x => x.Name).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await _db.Users
            .Where(x => x.Id == userId)
            .Select(x => x.ClientId)
            .FirstOrDefaultAsync();
    }

    private async Task<string> GenerateCompanyUserNameAsync(string companyName, string fullName)
    {
        var company = SlugPart(companyName, 10);
        var person = SlugPart(fullName, 10);
        var baseName = string.Join(".", new[] { company, person }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "client";
        }

        var userName = baseName;
        var suffix = 1;
        while (await _userManager.FindByNameAsync(userName) != null)
        {
            userName = $"{baseName}{suffix++}";
        }

        return userName;
    }

    private static string SlugPart(string value, int maxLength)
    {
        var map = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
            ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
            ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
            ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ы'] = "y",
            ['э'] = "e", ['ю'] = "yu", ['я'] = "ya", ['ь'] = "", ['ъ'] = ""
        };

        var builder = new System.Text.StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                builder.Append(ch);
            }
            else if (map.TryGetValue(ch, out var replacement))
            {
                builder.Append(replacement);
            }
        }

        var result = builder.ToString();
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    public class CompanyUserInput
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? UserName { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? Position { get; set; }

        public string? AccessProfile { get; set; } = AccessProfileService.Manager;

        public string? Country { get; set; } = "ru";
    }
}
