using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TechSupportRagBot.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Page();
            }

            if (User.IsInRole("Admin"))
            {
                return RedirectToPage("/Admin/Index");
            }

            if (User.IsInRole("Operator"))
            {
                return RedirectToPage("/Operator/Index");
            }

            return RedirectToPage("/Client/Index");
        }
    }
}
