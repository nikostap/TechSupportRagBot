using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Data;
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
}
