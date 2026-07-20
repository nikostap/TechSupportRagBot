using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720153000_NormalizeQaSerialNumbersAndEmbeddingMinimum")]
public partial class NormalizeQaSerialNumbersAndEmbeddingMinimum : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "QAEntries"
            SET "SerialNumber" = NULL
            WHERE lower(btrim(coalesce("SerialNumber", ''))) IN ('не указан', 'не указано', 'нет', 'н/д', 'n/a', 'na', '-', '—', '–');

            UPDATE "KnowledgeChunks"
            SET "SerialNumber" = NULL
            WHERE "QAEntryId" IS NOT NULL
              AND lower(btrim(coalesce("SerialNumber", ''))) IN ('не указан', 'не указано', 'нет', 'н/д', 'n/a', 'na', '-', '—', '–');

            UPDATE "ApiUsageRecords"
            SET "EstimatedCostRub" = 0.01
            WHERE lower("Operation") = 'embedding'
              AND "EstimatedCostRub" < 0.01;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Преобразование значений-заглушек в пустое поле намеренное и необратимое.
    }
}
