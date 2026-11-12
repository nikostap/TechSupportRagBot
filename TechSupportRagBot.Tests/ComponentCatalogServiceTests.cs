using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class ComponentCatalogServiceTests
{
    [Fact]
    public async Task ModelsWithoutPartNumberCanHaveDifferentSeriesAndArchivedDeviceStopsApplying()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var machine = new Machine { Name = "ALF 033", Model = "ALF-506", SerialNumber = "033", LicenseKey = "one" };
        var position = new MachinePosition { MachineModel = "ALF-506", Key = "servo", Name = "Servo", ComponentType = "Servo" };
        var a2 = new ComponentModel { Type = "Servo", Manufacturer = "Delta", Series = "A2", PartNumber = "", IsConfirmed = true };
        var b3 = new ComponentModel { Type = "Servo", Manufacturer = "Delta", Series = "B3", PartNumber = "", IsConfirmed = true };
        db.AddRange(machine, position, a2, b3);
        await db.SaveChangesAsync();
        db.ComponentInstallations.Add(new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = a2.Id, IsConfirmed = true });
        await db.SaveChangesAsync();

        var catalog = new ComponentCatalogService(db);
        Assert.Equal(a2.Id, Assert.Single(await catalog.ResolveAsync(machine)).Component.Id);
        a2.IsArchived = true;
        await db.SaveChangesAsync();
        Assert.Empty(await catalog.ResolveAsync(machine));
    }

    [Fact]
    public async Task PreconfiguredSerialAppliesAfterMachineIsCreatedAndOverridesModelRules()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var position = new MachinePosition { MachineModel = "ALF-506", Key = "tray.servo", Name = "Tray servo", ComponentType = "Servo" };
        var standard = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "A", PartNumber = "A-1", IsConfirmed = true };
        var special = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "B", PartNumber = "B-1", IsConfirmed = true };
        db.AddRange(position, standard, special);
        await db.SaveChangesAsync();
        db.ComponentInstallations.AddRange(
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = standard.Id, IsConfirmed = true },
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = special.Id,
                SpecificSerialNumber = "ALF033-2026-001", IsConfirmed = true });
        await db.SaveChangesAsync();

        var machine = new Machine { Name = "ALF 033", Model = "ALF-506", SerialNumber = "alf033-2026-001", LicenseKey = "one" };
        db.Machines.Add(machine);
        await db.SaveChangesAsync();

        Assert.Equal(special.Id, Assert.Single(await new ComponentCatalogService(db).ResolveAsync(machine)).Component.Id);
        machine.SerialNumber = "ALF033-2026-002";
        Assert.Equal(standard.Id, Assert.Single(await new ComponentCatalogService(db).ResolveAsync(machine)).Component.Id);
    }

    [Fact]
    public async Task ExactSerialOverridesApplicableRange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var machine = new Machine { Name = "ALF 033", Model = "ALF-506", SerialNumber = "033", LicenseKey = "one" };
        var position = new MachinePosition { MachineModel = "ALF-506", Key = "drive", Name = "Drive", ComponentType = "Servo" };
        var rangeDevice = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "A", PartNumber = "A", IsConfirmed = true };
        var exactDevice = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "B", PartNumber = "B", IsConfirmed = true };
        db.AddRange(machine, position, rangeDevice, exactDevice);
        await db.SaveChangesAsync();
        db.ComponentInstallations.AddRange(
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = rangeDevice.Id,
                SerialFrom = 1, SerialTo = 50, IsConfirmed = true },
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = exactDevice.Id,
                SpecificSerialNumber = "033", IsConfirmed = true });
        await db.SaveChangesAsync();

        Assert.Equal(exactDevice.Id, Assert.Single(await new ComponentCatalogService(db).ResolveAsync(machine)).Component.Id);
    }

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
            new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = shared.Id, MachineId = second.Id, IsConfirmed = true, IsActive = false },
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

    [Fact]
    public async Task ClientLibraryShowsOnlyPublishedApplicableDocuments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var machine = new Machine { Name = "ALF 033", Model = "ALF-506", SerialNumber = "033", LicenseKey = "one" };
        var other = new Machine { Name = "ALF 034", Model = "ALF-506", SerialNumber = "034", LicenseKey = "two" };
        var position = new MachinePosition { MachineModel = "ALF-506", Key = "tray.servo", Name = "Tray servo", ComponentType = "Servo" };
        var drive = new ComponentModel { Type = "Servo", Manufacturer = "Maker", Series = "A", PartNumber = "A-1", IsConfirmed = true };
        var author = new ApplicationUser { Id = "author", UserName = "author" };
        db.AddRange(machine, other, position, drive, author);
        await db.SaveChangesAsync();
        db.ComponentInstallations.Add(new ComponentInstallation { MachinePositionId = position.Id, ComponentModelId = drive.Id, IsConfirmed = true });
        KnowledgeDocument Doc(string name, int? machineId, int? componentId, bool published) => new()
        {
            OriginalFileName = name, StoredFileName = name, FilePath = name,
            UploadedByUserId = author.Id, DocumentKind = "ComponentManual",
            MachineId = machineId, ComponentModelId = componentId,
            ClientDownloadAllowed = published, ShowInClientLibrary = published,
            Status = published ? "Published" : "NeedsReview"
        };
        db.KnowledgeDocuments.AddRange(
            Doc("device.pdf", null, drive.Id, true),
            Doc("machine.pdf", machine.Id, null, true),
            Doc("other.pdf", other.Id, null, true),
            Doc("draft.pdf", machine.Id, null, false),
            new KnowledgeDocument
            {
                OriginalFileName = "global.pdf", StoredFileName = "global.pdf", FilePath = "global.pdf",
                UploadedByUserId = author.Id, DocumentKind = "SetupGuide", AppliesToAllMachines = true,
                ClientDownloadAllowed = true, ShowInClientLibrary = true, Status = "Published"
            });
        await db.SaveChangesAsync();

        var names = (await new ComponentCatalogService(db).ClientDocumentsAsync(machine))
            .Select(x => x.OriginalFileName).ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("device.pdf", names);
        Assert.Contains("machine.pdf", names);
        Assert.Contains("global.pdf", names);
    }

    [Fact]
    public async Task DocumentTargetsSelectSeveralModelsAndMachinesWithoutLeakingToOthers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var author = new ApplicationUser { Id = "author", UserName = "author" };
        var alf = new Machine { Name = "ALF", Model = "ALF-506", SerialNumber = "033", LicenseKey = "one" };
        var ms = new Machine { Name = "MS", Model = "MS", SerialNumber = "010", LicenseKey = "two" };
        var specific = new Machine { Name = "PRO 1", Model = "PRO", SerialNumber = "001", LicenseKey = "three" };
        var excluded = new Machine { Name = "PRO 2", Model = "PRO", SerialNumber = "002", LicenseKey = "four" };
        db.AddRange(author, alf, ms, specific, excluded);
        await db.SaveChangesAsync();
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            OriginalFileName = "shared.pdf", StoredFileName = "shared.pdf", FilePath = "shared.pdf",
            UploadedByUserId = author.Id, DocumentKind = "ComponentManual",
            ClientDownloadAllowed = true, ShowInClientLibrary = true, Status = "Published",
            Targets = [new() { MachineModel = "ALF-506" }, new() { MachineModel = "MS" },
                new() { MachineId = specific.Id }]
        });
        await db.SaveChangesAsync();
        var catalog = new ComponentCatalogService(db);
        Assert.Single(await catalog.ClientDocumentsAsync(alf));
        Assert.Single(await catalog.ClientDocumentsAsync(ms));
        Assert.Single(await catalog.ClientDocumentsAsync(specific));
        Assert.Empty(await catalog.ClientDocumentsAsync(excluded));
    }
}
