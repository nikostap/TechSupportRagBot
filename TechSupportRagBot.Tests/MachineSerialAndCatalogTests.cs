using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Pages.Admin;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class MachineSerialAndCatalogTests
{
    [Theory]
    [InlineData("1", "001")]
    [InlineData("35", "035")]
    [InlineData("033", "033")]
    [InlineData("1234", "1234")]
    [InlineData("ALF-35", "ALF-35")]
    public void MachineSerialPadsOnlyShortNumericValues(string input, string expected) =>
        Assert.Equal(expected, SerialNumberFormatter.NormalizeMachine(input));

    [Theory]
    [InlineData("1", "001")]
    [InlineData("5-30", "005-030")]
    [InlineData(" 01 - 1234 ", "001-1234")]
    [InlineData("ALF-35", null)]
    public void QaSerialPadsRangeBoundsAndKeepsInvalidScopeEmpty(string input, string? expected) =>
        Assert.Equal(expected, SerialNumberFormatter.NormalizeQaRule(input));

    [Fact]
    public async Task ModelListIsSortedAndRemovalPersistsWithoutResettingDefaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();
        var settings = new SystemSettingsService(db, Options.Create(new RagOptions()), Options.Create(new StorageOptions()));

        var defaults = await settings.GetMachineModelsAsync();
        Assert.Contains("MS", defaults);
        Assert.Equal(defaults.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), defaults);

        Assert.True(await settings.AddMachineModelAsync("ZZ"));
        Assert.False(await settings.AddMachineModelAsync("zz"));
        Assert.True(await settings.RemoveMachineModelAsync("ms"));
        Assert.False(await settings.RemoveMachineModelAsync("ms"));

        var reloaded = new SystemSettingsService(db, Options.Create(new RagOptions()), Options.Create(new StorageOptions()));
        var models = await reloaded.GetMachineModelsAsync();
        Assert.Contains("ZZ", models);
        Assert.DoesNotContain("MS", models);
        Assert.Equal(models.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), models);
    }

    [Fact]
    public async Task SerialIsUniqueWithinModelButCanRepeatInAnotherModel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        db.Machines.AddRange(
            new Machine { Name = "Model L", Model = "L", SerialNumber = "000", LicenseKey = "one" },
            new Machine { Name = "Model M", Model = "M", SerialNumber = "000", LicenseKey = "two" });
        await db.SaveChangesAsync();

        db.Machines.Add(new Machine { Name = "Another M", Model = "M", SerialNumber = "000", LicenseKey = "three" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void MigrationReplacesGlobalSerialIndexWithModelScopedIndex()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=script_only;Username=unused;Password=unused").Options);
        var script = db.GetService<IMigrator>().GenerateScript(
            "20261112120000_AddKnowledgeDocumentTargets", "20261117100000_MachineSerialUniquePerModel");
        Assert.Contains("DROP INDEX \"IX_Machines_SerialNumber\"", script);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Machines_Model_SerialNumber\"", script);
    }

    [Fact]
    public async Task MachineFormAcceptsSameSerialForDifferentModelAndExplainsDuplicate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Machines.Add(new Machine { Name = "Model L", Model = "L", SerialNumber = "000", LicenseKey = "one" });
        await db.SaveChangesAsync();

        var settings = new SystemSettingsService(db, Options.Create(new RagOptions()), Options.Create(new StorageOptions()));
        MachinesModel CreatePage() => new(db, settings)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Input = new MachinesModel.MachineInput { Name = "M", Model = "M", SerialNumber = "000", Description = "Test" }
        };
        var first = CreatePage();
        Assert.IsType<RedirectToPageResult>(await first.OnPostAsync());
        Assert.Equal(2, await db.Machines.CountAsync());

        var duplicate = CreatePage();
        Assert.IsType<PageResult>(await duplicate.OnPostAsync());
        Assert.False(duplicate.ModelState.IsValid);
        Assert.Equal(2, await db.Machines.CountAsync());
    }
}
