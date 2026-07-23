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
    private readonly IWebHostEnvironment _environment;

    public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IHttpClientFactory httpClientFactory, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _environment = environment;
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
            ModelState.AddModelError(string.Empty, "Подтвердите, что вы не робот, и повторите попытку.");
            return RedirectToPage("/Index", new { area = "", loginError = "captcha" });
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.UserName.Trim(),
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(Input.UserName.Trim());
            if (user?.MustChangePassword == true)
            {
                return RedirectToPage("/Account/ChangePassword", new { area = "Identity" });
            }

            return LocalRedirect(ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
        return RedirectToPage("/Index", new { area = "", loginError = "invalid" });
    }

    private async Task<bool> IsCaptchaValidAsync()
    {
        if (_environment.IsDevelopment() || IsLocalRequest()) return true;

        var serverKey = _configuration["SmartCaptcha:ServerKey"];
        var token = Request.Form["smart-token"].ToString();
        if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = serverKey,
                ["token"] = token,
                ["ip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
            });
            var response = await _httpClientFactory.CreateClient("SmartCaptcha")
                .PostAsync("https://smartcaptcha.cloud.yandex.ru/validate", content, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<CaptchaValidationResponse>(cancellationToken: HttpContext.RequestAborted);
            return string.Equals(result?.Status, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested) { return false; }
    }

    private bool IsLocalRequest()
    {
        var host = Request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal);
    }

    private sealed class CaptchaValidationResponse
    {
        public string? Status { get; init; }
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
