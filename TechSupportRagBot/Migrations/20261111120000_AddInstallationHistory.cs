using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111120000_AddInstallationHistory")]
public partial class AddInstallationHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsActive", "ComponentInstallations", type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<DateTime>("RetiredAt", "ComponentInstallations", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("IsActive", "ComponentInstallations");
        migrationBuilder.DropColumn("RetiredAt", "ComponentInstallations");
    }
}
