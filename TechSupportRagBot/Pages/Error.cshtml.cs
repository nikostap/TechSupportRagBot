using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public IActionResult OnGet() => Handle();

        public IActionResult OnPost() => Handle();

        private IActionResult Handle()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new
                {
                    ok = false,
                    error = UiText.T(HttpContext, "UnexpectedErrorBody"),
                    requestId = RequestId
                }) { StatusCode = StatusCodes.Status500InternalServerError };
            }

            return Page();
        }
    }

}
