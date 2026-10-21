using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class BrowserPushTests
{
    [Fact]
    public async Task SubscriptionBelongsToOneAccount_AndCanBeReleasedByTheSameBrowser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = "first", UserName = "first" },
            new ApplicationUser { Id = "second", UserName = "second" });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Push:PublicKey"] = "test-public-key",
            ["Push:PrivateKey"] = "test-private-key"
        }).Build();
        var push = new BrowserPushService(db, config, NullLogger<BrowserPushService>.Instance);
        const string endpoint = "https://fcm.googleapis.com/fcm/send/device";
        var p256dh = new string('A', 65);
        var auth = new string('B', 24);

        Assert.True(await push.SubscribeAsync("first", endpoint, p256dh, auth, default));
        Assert.True(await push.IsSubscribedAsync("first", endpoint, default));
        Assert.False(await push.IsSubscribedAsync("second", endpoint, default));

        await push.ReleaseOtherAccountAsync("second", endpoint, p256dh, "wrong-key-value", default);
        Assert.True(await push.IsSubscribedAsync("first", endpoint, default));

        await push.ReleaseOtherAccountAsync("second", endpoint, p256dh, auth, default);
        Assert.False(await push.IsSubscribedAsync("first", endpoint, default));
        Assert.True(await push.SubscribeAsync("second", endpoint, p256dh, auth, default));
        Assert.True(await push.IsSubscribedAsync("second", endpoint, default));
        Assert.False(await push.IsSubscribedAsync("first", endpoint, default));
    }

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
        foreach (var key in new[] { "PushEnable", "PushDisable", "PushDenied", "PushContextTitle",
            "PushContextBody", "PushAttachmentPreview", "ProfileEmailNotifications",
            "ProfileEmailNotificationsHelp", "ProfileEmailNotificationsEnable",
            "CookieNotice", "CookieAccept", "CookieReject", "PrivacyPolicy", "PrivacyRightsBody" })
            Assert.NotEqual(key, UiText.ForLanguage(language, key));
    }
}
