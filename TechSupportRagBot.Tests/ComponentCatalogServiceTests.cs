using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class ComponentCatalogServiceTests
{
    [Fact]
    public async Task SharedDeviceIsReusedButLocalReplacementWins()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var first = new Machine { Name = "ALF 033", Model = "ALF-506", SerialNumber = "033", LicenseKey = "one" };
        var second = new Machine { Name = "ALF 034", Model = "ALF-506", SerialNumber = "034", LicenseKey = "two" };
        var position = new MachinePosition { MachineModel = "ALF-506", Key = "tray.servo", Name = "Tray servo", ComponentType = "Servo" };
        var shared = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "A", PartNumber = "A-1", IsConfirmed = true };
        var replacement = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "B", PartNumber = "B-1", IsConfirmed = true };
        db.AddRange(first, second, position, shared, replacement);
        await db.SaveChangesAsync();
        db.ComponentInstallations.AddRange(
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = shared.Id, IsConfirmed = true },
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = replacement.Id, MachineId = second.Id, IsConfirmed = true });
        await db.SaveChangesAsync();

        var catalog = new ComponentCatalogService(db);
        Assert.Equal(shared.Id, Assert.Single(await catalog.ResolveAsync(first)).Component.Id);
        Assert.Equal(replacement.Id, Assert.Single(await catalog.ResolveAsync(second)).Component.Id);
    }

    [Fact]
    public async Task ConflictingRangesDoNotRevealEitherManual()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var machine = new Machine { Name = "ALF 033", Model = "ALF", SerialNumber = "033", LicenseKey = "one" };
        var position = new MachinePosition { MachineModel = "ALF", Key = "tray.servo", Name = "Tray servo", ComponentType = "Servo" };
        var first = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "A", PartNumber = "A-1", IsConfirmed = true };
        var second = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "B", PartNumber = "B-1", IsConfirmed = true };
        db.AddRange(machine, position, first, second);
        await db.SaveChangesAsync();
        db.ComponentInstallations.AddRange(
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = first.Id, SerialFrom = 1, SerialTo = 50, IsConfirmed = true },
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = second.Id, SerialFrom = 30, SerialTo = 60, IsConfirmed = true });
        await db.SaveChangesAsync();

        Assert.Empty(await new ComponentCatalogService(db).ResolveAsync(machine));
    }
}
