using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261102100000_RemoveQaClientApproval")]
public partial class RemoveQaClientApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "AvailableToClients", table: "QAEntries");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "AvailableToClients",
            table: "QAEntries",
            type: "boolean",
            nullable: false,
            defaultValue: false);
}
