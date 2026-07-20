using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720163000_RecalculateAiTunnelDeepSeekCosts")]
public partial class RecalculateAiTunnelDeepSeekCosts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "ApiUsageRecords"
            SET "EstimatedCostRub" = round((
                ("InputTokens" * 42.0 / 1000000.0) +
                ("OutputTokens" * 140.0 / 1000000.0)
            ) * 1.10, 6)
            WHERE lower("Provider") = 'aitunnel'
              AND lower("Model") LIKE '%deepseek-chat%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Исторические значения пересчитываются в сторону более консервативной оценки.
    }
}
