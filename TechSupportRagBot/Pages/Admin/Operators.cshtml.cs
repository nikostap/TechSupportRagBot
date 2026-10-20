using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TechSupportRagBot.Pages.Admin;

// Keep old bookmarks usable after employee management moved to one page.
[Authorize]
public class OperatorsModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Admins");

    public IActionResult OnPost() => RedirectToPage("/Admin/Admins");
}
