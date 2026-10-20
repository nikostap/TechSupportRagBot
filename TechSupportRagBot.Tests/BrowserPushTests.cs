using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class BrowserPushTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/example", true)]
    [InlineData("https://web.push.apple.com/example", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/example", true)]
    [InlineData("https://db3.notify.windows.com/example", true)]
    [InlineData("http://fcm.googleapis.com/example", false)]
    [InlineData("https://fcm.googleapis.com.attacker.example/", false)]
    [InlineData("https://127.0.0.1/example", false)]
    [InlineData("https://fcm.googleapis.com:8443/example", false)]
    public void BrowserPushEndpoints_AllowOnlyTrustedHttpsServices(string endpoint, bool expected)
    {
        Assert.Equal(expected, BrowserPushService.IsAllowedEndpoint(endpoint));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void NewInterfaceWords_HaveTranslations(string language)
    {
        foreach (var key in new[] { "PushEnable", "PushDisable", "PushDenied", "PushTicketBody",
            "CookieNotice", "CookieAccept", "CookieReject", "PrivacyPolicy", "PrivacyRightsBody" })
            Assert.NotEqual(key, UiText.ForLanguage(language, key));
    }
}
