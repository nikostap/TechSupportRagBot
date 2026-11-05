using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Pages.Admin;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class MonthlyPasswordTests
{
    [Theory]
    [InlineData(0, "ALF", "033", 2026, 11, "867245")]
    [InlineData(1, "M", "288", 2026, 12, "813509")]
    [InlineData(11, "PRO", "017", 2027, 1, "865379")]
    public void MatchesClassEditorCalculation(int id, string code, string serial, int year, int month, string expected) =>
        Assert.Equal(expected, MonthlyPasswordGenerator.Generate(id, code, serial, year, month));

    [Fact]
    public void LegacyAndUnmappedMachinesCannotBeEmailed()
    {
        Assert.Throws<InvalidOperationException>(() => MonthlyPasswordGenerator.Generate(1, "M", "287", 2026, 11));
        var ids = MonthlyPasswordGenerator.DefaultModels.ToDictionary(x => x.Code, x => x.Id);
        ids["MS"] = 12;
        Assert.False(MonthlyPasswordGenerator.TryForMachine(
            new Machine { Model = "ZZ", SerialNumber = "074" }, new DateOnly(2026, 11, 1), ids, out _));
        Assert.True(MonthlyPasswordGenerator.TryForMachine(
            new Machine { Model = "MS", SerialNumber = "074" },
            new DateOnly(2026, 11, 1), ids, out _));
    }

    [Fact]
    public async Task Alf506UsesAlfOnlyForPasswordsAndPageOpensOnCurrentMonth()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var settings = NewSettings(db);
        var ids = await settings.GetMonthlyPasswordModelIdsAsync();
        var machine = new Machine { Model = "ALF-506", SerialNumber = "033" };
        Assert.True(MonthlyPasswordGenerator.TryForMachine(machine, new DateOnly(2026, 11, 1), ids, out var password));
        Assert.Equal("867245", password);
        Assert.Equal("ALF-506", machine.Model);

        var page = new MonthlyPasswordsModel(settings)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            ModelName = "ALF-506", SerialNumber = "033", Year = 2026, Month = 11
        };
        await page.OnGetAsync();
        Assert.Contains("ALF-506", page.Models);
        Assert.Equal(password, page.Result);

        var initial = new MonthlyPasswordsModel(settings)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        await initial.OnGetAsync();
        var moscowToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        Assert.Equal(moscowToday.Year, initial.Year);
        Assert.Equal(moscowToday.Month, initial.Month);
    }

    [Fact]
    public async Task EmailRequiresOptInAndRunsOnceForNextMonth()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var client = new Client { Name = "Test", ContactEmail = "client@example.test" };
        var machine = new Machine { Name = "Machine", Model = "ALF", SerialNumber = "033", LicenseKey = "test" };
        db.Clients.Add(client);
        db.Machines.Add(machine);
        await db.SaveChangesAsync();
        db.ClientMachines.Add(new ClientMachine { ClientId = client.Id, MachineId = machine.Id });
        await db.SaveChangesAsync();
        var sender = new CaptureSender();
        var settings = NewSettings(db);
        var service = new ClientAutomationService(db, sender, settings, NullLogger<ClientAutomationService>.Instance);
        await service.ProcessAsync();
        Assert.Empty(sender.Messages);

        client.MonthlyPasswordEmailEnabled = true;
        client.MonthlyPasswordSendDay = 1;
        await db.SaveChangesAsync();
        await service.ProcessAsync();
        await service.ProcessAsync();
        Assert.Single(sender.Messages);
        Assert.Single(db.MonthlyPasswordEmailLogs);
    }

    [Fact]
    public async Task ExpiredSupportLicenseRenewsOnlyWhenClientOptsIn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var client = new Client { Name = "Test" };
        var machine = new Machine { Name = "Machine", Model = "ALF", SerialNumber = "033", LicenseKey = "test" };
        db.Clients.Add(client);
        db.Machines.Add(machine);
        await db.SaveChangesAsync();
        var originalExpiry = DateTime.UtcNow.AddDays(-1);
        var license = new License { ClientId = client.Id, MachineId = machine.Id,
            Key = "license-test", IsActivated = true, ExpiresAt = originalExpiry };
        db.Licenses.Add(license);
        await db.SaveChangesAsync();
        var service = new ClientAutomationService(db, new CaptureSender(), NewSettings(db),
            NullLogger<ClientAutomationService>.Instance);
        await service.ProcessAsync();
        Assert.Equal(originalExpiry, license.ExpiresAt);

        client.AutoRenewSupportLicense = true;
        await db.SaveChangesAsync();
        await service.ProcessAsync();
        Assert.True(license.ExpiresAt > DateTime.UtcNow.AddMonths(11));
    }

    [Fact]
    public async Task SiteModelAdditionGetsStablePasswordId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var settings = NewSettings(db);
        Assert.True(await settings.AddMachineModelAsync("ZZ"));
        var ids = await settings.GetMonthlyPasswordModelIdsAsync();
        Assert.Equal(13, ids["ZZ"]);
        Assert.True(MonthlyPasswordGenerator.TryForMachine(new Machine { Model = "ZZ", SerialNumber = "001" },
            new DateOnly(2027, 1, 1), ids, out _));
        Assert.True(await settings.RemoveMachineModelAsync("ZZ"));
        Assert.True(await settings.AddMachineModelAsync("YY"));
        Assert.Equal(14, (await settings.GetMonthlyPasswordModelIdsAsync())["YY"]);
    }

    private static SystemSettingsService NewSettings(ApplicationDbContext db) =>
        new(db, Options.Create(new RagOptions()), Options.Create(new StorageOptions()));

    private sealed class CaptureSender : IEmailSender
    {
        public List<string> Messages { get; } = [];
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Messages.Add(htmlBody);
            return Task.CompletedTask;
        }
    }
}
