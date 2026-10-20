using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class PasswordRecoveryThrottle(IMemoryCache cache)
{
    private readonly object _gate = new();

    public bool AllowRequest(string email, string? remoteIp)
    {
        var emailKey = "reset:email:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant())));
        var ipKey = "reset:ip:" + (remoteIp ?? "unknown");
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var emailState = cache.Get<ThrottleState>(emailKey) ?? new ThrottleState(0, DateTimeOffset.MinValue);
            var ipState = cache.Get<ThrottleState>(ipKey) ?? new ThrottleState(0, now);
            if (emailState.Count >= 5 || ipState.Count >= 20 || now - emailState.LastRequest < TimeSpan.FromMinutes(1))
            {
                return false;
            }

            cache.Set(emailKey, new ThrottleState(emailState.Count + 1, now), TimeSpan.FromHours(1));
            cache.Set(ipKey, new ThrottleState(ipState.Count + 1, now), TimeSpan.FromHours(1));
            return true;
        }
    }

    private sealed record ThrottleState(int Count, DateTimeOffset LastRequest);
}

public sealed class PasswordRecoveryService(
    UserManager<ApplicationUser> users,
    SmtpEmailSender emailSender,
    IDataProtectionProvider dataProtection,
    PasswordRecoveryThrottle throttle,
    ILogger<PasswordRecoveryService> logger)
{
    private const string LoginProvider = "ClassEngineeringSupport";
    private const string TokenName = "PasswordRecoveryCode";
    private readonly IDataProtector _protector = dataProtection.CreateProtector("PasswordRecoveryCode.v1");

    public async Task RequestAsync(HttpContext context, string email)
    {
        email = email.Trim();
        if (!throttle.AllowRequest(email, context.Connection.RemoteIpAddress?.ToString())) return;

        var user = await users.FindByEmailAsync(email);
        if (user?.Email == null) return;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var state = new RecoveryState(
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code))),
            DateTimeOffset.UtcNow.AddMinutes(10),
            0);
        var stored = await users.SetAuthenticationTokenAsync(user, LoginProvider, TokenName,
            _protector.Protect(JsonSerializer.Serialize(state)));
        if (!stored.Succeeded)
        {
            logger.LogWarning("Could not store a password recovery code for user {UserId}.", user.Id);
            return;
        }

        try
        {
            var body = string.Format(UiText.T(context, "RecoveryEmailBody"), code);
            await emailSender.SendAsync(user.Email, UiText.T(context, "RecoveryEmailSubject"), body,
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
            logger.LogWarning(ex, "Could not send a password recovery email for user {UserId}.", user.Id);
        }
    }

    public async Task<ApplicationUser?> VerifyAsync(string email, string code)
    {
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) return null;
        var user = await users.FindByEmailAsync(email.Trim());
        if (user == null) return null;

        var value = await users.GetAuthenticationTokenAsync(user, LoginProvider, TokenName);
        if (string.IsNullOrWhiteSpace(value)) return null;

        RecoveryState? state;
        try { state = JsonSerializer.Deserialize<RecoveryState>(_protector.Unprotect(value)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
            return null;
        }

        if (state == null || state.ExpiresAt <= DateTimeOffset.UtcNow || state.Attempts >= 5)
        {
            await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
            return null;
        }

        byte[] expected;
        try { expected = Convert.FromBase64String(state.CodeHash); }
        catch (FormatException)
        {
            await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
            return null;
        }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        if (CryptographicOperations.FixedTimeEquals(actual, expected)) return user;

        state = state with { Attempts = state.Attempts + 1 };
        if (state.Attempts >= 5)
        {
            await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
        }
        else
        {
            await users.SetAuthenticationTokenAsync(user, LoginProvider, TokenName,
                _protector.Protect(JsonSerializer.Serialize(state)));
        }
        return null;
    }

    public async Task ConsumeAsync(ApplicationUser user) =>
        await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);

    private sealed record RecoveryState(string CodeHash, DateTimeOffset ExpiresAt, int Attempts);
}
