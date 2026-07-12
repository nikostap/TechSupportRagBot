using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260712143000_ReclassifyWeekendWorkTime")]
public partial class ReclassifyWeekendWorkTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "OperatorChatTimeEntries"
            SET "OvertimeSeconds" = "OvertimeSeconds" + "WorkSeconds",
                "WorkSeconds" = 0
            WHERE EXTRACT(ISODOW FROM ("StartedAt" AT TIME ZONE 'Europe/Moscow')) IN (6, 7)
              AND "WorkSeconds" > 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The previous incorrect classification cannot be reconstructed unambiguously.
    }
}
