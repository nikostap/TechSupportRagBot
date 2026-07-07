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

        if (await _userManager.FindByNameAsync(Input.UserName.Trim()) != null)
        {
            ModelState.AddModelError(string.Empty, "Этот логин уже занят.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var password = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = Input.UserName.Trim(),
            Email = Input.Email,
            FullName = Input.FullName.Trim(),
            Position = Input.Position,
            Country = Input.Country,
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

    public class CompanyUserInput
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Position { get; set; }

        public string? Country { get; set; }
    }
}
