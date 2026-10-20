using System.Net;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class AccountWelcomeEmailService(
    IEmailSender sender,
    ILogger<AccountWelcomeEmailService> logger)
{
    public async Task SendIfAddressAvailableAsync(ApplicationUser user, string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;

        var language = user.Country == "en" ? "en" : "ru";
        var body = string.Format(UiText.ForLanguage(language, "WelcomeMailBody"),
            WebUtility.HtmlEncode(user.UserName), WebUtility.HtmlEncode(temporaryPassword),
            "https://support.classeng.com/");
        try
        {
            await sender.SendAsync(user.Email, UiText.ForLanguage(language, "WelcomeMailSubject"),
                body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send account welcome email for user {UserId}.", user.Id);
        }
    }
}
