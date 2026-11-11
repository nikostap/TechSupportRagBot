using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111140000_AddSpecificInstallationSerial")]
public partial class AddSpecificInstallationSerial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("SpecificSerialNumber", "ComponentInstallations",
            type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.CreateIndex("IX_ComponentInstallations_MachinePositionId_SpecificSerialNumber",
            "ComponentInstallations", new[] { "MachinePositionId", "SpecificSerialNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_ComponentInstallations_MachinePositionId_SpecificSerialNumber", "ComponentInstallations");
        migrationBuilder.DropColumn("SpecificSerialNumber", "ComponentInstallations");
    }
}
