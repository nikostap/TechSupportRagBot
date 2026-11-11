using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Areas.Identity.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        return RedirectToPage("/Index", new { area = "" });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return RedirectToPage("/Index", new { area = "", loginError = "invalid" });
        }

        if (!await IsCaptchaValidAsync())
        {
            ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "CaptchaRetry"));
            return RedirectToPage("/Index", new { area = "", loginError = "captcha" });
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.UserName.Trim(),
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(Input.UserName.Trim());
            if (user?.MustChangePassword == true)
            {
                return RedirectToPage("/Account/ChangePassword", new { area = "Identity" });
            }

            return LocalRedirect(ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, TechSupportRagBot.Services.UiText.T(HttpContext, "InvalidCredentials"));
        return RedirectToPage("/Index", new { area = "", loginError = "invalid" });
    }

    private async Task<bool> IsCaptchaValidAsync()
    {
        if (!TechSupportRagBot.Services.LoginCaptchaPolicy.IsRequired(HttpContext)) return true;

        var serverKey = _configuration["Recaptcha:SecretKey"];
        var token = Request.Form["g-recaptcha-response"].ToString();
        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = serverKey,
                ["response"] = token
            });
            var response = await _httpClientFactory.CreateClient("Recaptcha")
                .PostAsync("https://www.google.com/recaptcha/api/siteverify", content, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<CaptchaValidationResponse>(cancellationToken: HttpContext.RequestAborted);
            return result?.Success == true;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { return false; }
    }

    private sealed class CaptchaValidationResponse
    {
        public bool Success { get; init; }
    }

    public class InputModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
