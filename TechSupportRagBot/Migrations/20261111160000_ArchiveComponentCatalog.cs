using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111160000_ArchiveComponentCatalog")]
public partial class ArchiveComponentCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsArchived", "ComponentModels", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("IsArchived", "MachinePositions", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("IsArchived", "PositionTemplates", type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("IsArchived", "PositionTemplates");
        migrationBuilder.DropColumn("IsArchived", "MachinePositions");
        migrationBuilder.DropColumn("IsArchived", "ComponentModels");
    }
}
