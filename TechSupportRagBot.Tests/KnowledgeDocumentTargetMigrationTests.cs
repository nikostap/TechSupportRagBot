using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

namespace TechSupportRagBot.Tests;

public sealed class KnowledgeDocumentTargetMigrationTests
{
    [Fact]
    public void MigrationCreatesTargetsAndCopiesExistingSingleScope()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=script_only;Username=unused;Password=unused").Options;
        using var db = new ApplicationDbContext(options);
        var script = db.GetService<IMigrator>().GenerateScript(
            "20261111190000_AddPreparedManualSidecar", "20261112120000_AddKnowledgeDocumentTargets");
        Assert.Contains("CREATE TABLE \"KnowledgeDocumentTargets\"", script);
        Assert.Contains("INSERT INTO \"KnowledgeDocumentTargets\"", script);
        Assert.Contains("\"MachineId\" IS NOT NULL", script);
    }
}
