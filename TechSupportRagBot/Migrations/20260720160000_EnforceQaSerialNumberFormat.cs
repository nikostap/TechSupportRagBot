using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720160000_EnforceQaSerialNumberFormat")]
public partial class EnforceQaSerialNumberFormat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "QAEntries"
            SET "SerialNumber" = CASE
                WHEN btrim(coalesce("SerialNumber", '')) ~ '^[0-9]+[[:space:]]*-[[:space:]]*[0-9]+$'
                    THEN regexp_replace(btrim("SerialNumber"), '[[:space:]]*-[[:space:]]*', '-', 'g')
                WHEN btrim(coalesce("SerialNumber", '')) ~ '^[0-9]+$'
                    THEN btrim("SerialNumber")
                ELSE NULL
            END;

            UPDATE "KnowledgeChunks"
            SET "SerialNumber" = CASE
                WHEN btrim(coalesce("SerialNumber", '')) ~ '^[0-9]+[[:space:]]*-[[:space:]]*[0-9]+$'
                    THEN regexp_replace(btrim("SerialNumber"), '[[:space:]]*-[[:space:]]*', '-', 'g')
                WHEN btrim(coalesce("SerialNumber", '')) ~ '^[0-9]+$'
                    THEN btrim("SerialNumber")
                ELSE NULL
            END
            WHERE "QAEntryId" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Очистка некорректных значений намеренная и необратимая.
    }
}
