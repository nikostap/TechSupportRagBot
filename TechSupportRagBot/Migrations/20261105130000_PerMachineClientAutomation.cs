using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105130000_PerMachineClientAutomation")]
public partial class PerMachineClientAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClientMachineAutomations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClientId = table.Column<int>(type: "integer", nullable: false),
                MachineId = table.Column<int>(type: "integer", nullable: false),
                MonthlyPasswordEmailEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                AutoRenewSupportLicense = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClientMachineAutomations", x => x.Id);
                table.ForeignKey("FK_ClientMachineAutomations_Clients_ClientId", x => x.ClientId,
                    "Clients", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_ClientMachineAutomations_Machines_MachineId", x => x.MachineId,
                    "Machines", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_ClientMachineAutomations_ClientId_MachineId",
            "ClientMachineAutomations", new[] { "ClientId", "MachineId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ClientMachineAutomations_MachineId",
            "ClientMachineAutomations", "MachineId");

        migrationBuilder.Sql("""
            INSERT INTO "ClientMachineAutomations" ("ClientId", "MachineId", "MonthlyPasswordEmailEnabled", "AutoRenewSupportLicense")
            SELECT c."Id", pairs."MachineId", c."MonthlyPasswordEmailEnabled", c."AutoRenewSupportLicense"
            FROM "Clients" c
            JOIN (
                SELECT "ClientId", "MachineId" FROM "ClientMachines"
                UNION
                SELECT "ClientId", "MachineId" FROM "Licenses"
            ) pairs ON pairs."ClientId" = c."Id"
            WHERE c."MonthlyPasswordEmailEnabled" OR c."AutoRenewSupportLicense";
            """);

        migrationBuilder.DropColumn("MonthlyPasswordEmailEnabled", "Clients");
        migrationBuilder.DropColumn("MonthlyPasswordSendDay", "Clients");
        migrationBuilder.DropColumn("AutoRenewSupportLicense", "Clients");
        migrationBuilder.DropIndex("IX_MonthlyPasswordEmailLogs_ClientId_Year_Month", "MonthlyPasswordEmailLogs");
        migrationBuilder.AddColumn<int>("MachineId", "MonthlyPasswordEmailLogs", type: "integer", nullable: true);
        migrationBuilder.CreateIndex("IX_MonthlyPasswordEmailLogs_ClientId_MachineId_Year_Month",
            "MonthlyPasswordEmailLogs", new[] { "ClientId", "MachineId", "Year", "Month" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_MonthlyPasswordEmailLogs_ClientId_MachineId_Year_Month", "MonthlyPasswordEmailLogs");
        migrationBuilder.DropColumn("MachineId", "MonthlyPasswordEmailLogs");
        migrationBuilder.DropTable("ClientMachineAutomations");
        migrationBuilder.AddColumn<bool>("MonthlyPasswordEmailEnabled", "Clients", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("MonthlyPasswordSendDay", "Clients", type: "integer", nullable: false, defaultValue: 28);
        migrationBuilder.AddColumn<bool>("AutoRenewSupportLicense", "Clients", type: "boolean", nullable: false, defaultValue: false);
    }
}
