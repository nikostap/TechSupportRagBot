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
public class ClientsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TicketDeletionService _ticketDeletion;

    public ClientsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TicketDeletionService ticketDeletion)
    {
        _db = db;
        _userManager = userManager;
        _ticketDeletion = ticketDeletion;
    }

    [BindProperty]
    public ClientInput Input { get; set; } = new();

    public List<TechSupportRagBot.Models.Client> Clients { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var userName = await GenerateClientUserNameAsync(Input.CompanyName, Input.FullName);

        var client = new TechSupportRagBot.Models.Client
        {
            Name = Input.CompanyName.Trim(),
            ContactEmail = Input.ContactEmail,
            ContactPhone = Input.ContactPhone
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        var password = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = Input.ContactEmail,
            FullName = Input.FullName.Trim(),
            ClientId = client.Id,
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

    public async Task<IActionResult> OnPostUpdateUserAsync(int clientId, string userId, ClientUserInput input)
    {
        var client = await _db.Clients.FindAsync(clientId);
        var user = await _userManager.FindByIdAsync(userId);
        if (client == null || user == null || user.ClientId != clientId || !await _userManager.IsInRoleAsync(user, "Client"))
        {
            return RedirectToPage();
        }

        var existing = await _userManager.FindByNameAsync(input.UserName.Trim());
        if (existing != null && existing.Id != user.Id)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
            await LoadAsync();
            return Page();
        }

        client.Name = input.CompanyName.Trim();
        client.ContactEmail = input.Email;
        client.ContactPhone = input.Phone;
        user.FullName = input.FullName.Trim();
        user.UserName = input.UserName.Trim();
        user.Email = input.Email;
        user.NormalizedUserName = _userManager.NormalizeName(user.UserName);
        user.NormalizedEmail = _userManager.NormalizeEmail(user.Email);

        await _db.SaveChangesAsync();
        await _userManager.UpdateAsync(user);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(string userId, string password)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null && await _userManager.IsInRoleAsync(user, "Client") && !string.IsNullOrWhiteSpace(password))
        {
            await _userManager.RemovePasswordAsync(user);
            var result = await _userManager.AddPasswordAsync(user, password);
            if (result.Succeeded)
            {
                user.IssuedPassword = password;
                user.MustChangePassword = false;
                await _userManager.UpdateAsync(user);
            }
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var client = await _db.Clients
            .Include(x => x.Users)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (client == null)
        {
            return RedirectToPage();
        }

        var users = client.Users.ToList();
        var usersToDelete = new List<ApplicationUser>();

        foreach (var user in users)
        {
            if (await _userManager.IsInRoleAsync(user, "Client"))
            {
                usersToDelete.Add(user);
            }
            else
            {
                user.ClientId = null;
                await _userManager.UpdateAsync(user);
            }
        }

        await _ticketDeletion.DeleteTicketsForUsersAsync(usersToDelete.Select(x => x.Id));

        var licenses = await _db.Licenses.Where(x => x.ClientId == id).ToListAsync();
        var accesses = await _db.ClientMachines.Where(x => x.ClientId == id).ToListAsync();
        _db.Licenses.RemoveRange(licenses);
        _db.ClientMachines.RemoveRange(accesses);
        await _db.SaveChangesAsync();

        foreach (var user in usersToDelete)
        {
            await _userManager.DeleteAsync(user);
        }

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Clients = await _db.Clients
            .Include(x => x.Users)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private async Task<string> GenerateClientUserNameAsync(string companyName, string fullName)
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

    public class ClientInput
    {
        [Required]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [EmailAddress]
        public string? ContactEmail { get; set; }

        public string? ContactPhone { get; set; }
    }

    public class ClientUserInput
    {
        [Required]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Phone { get; set; }
    }
}
