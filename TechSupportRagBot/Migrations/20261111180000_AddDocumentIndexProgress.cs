using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111180000_AddDocumentIndexProgress")]
public partial class AddDocumentIndexProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("IndexStatus", "KnowledgeDocuments", type: "character varying(20)", maxLength: 20,
            nullable: false, defaultValue: "NotStarted");
        migrationBuilder.AddColumn<int>("IndexTotalChunks", "KnowledgeDocuments", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("IndexCompletedChunks", "KnowledgeDocuments", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("IndexErrorCode", "KnowledgeDocuments", type: "character varying(40)", maxLength: 40,
            nullable: true);
        migrationBuilder.AddColumn<DateTime>("IndexUpdatedAt", "KnowledgeDocuments", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex("IX_KnowledgeDocuments_IndexStatus", "KnowledgeDocuments", "IndexStatus");
        migrationBuilder.Sql("""
            UPDATE "KnowledgeDocuments" SET
                "IndexStatus" = CASE
                    WHEN "Status" IN ('Готов', 'Published') AND EXISTS
                        (SELECT 1 FROM "KnowledgeChunks" c WHERE c."KnowledgeDocumentId" = "KnowledgeDocuments"."Id") THEN 'Ready'
                    WHEN "Status" = 'Без текста' THEN 'NoText'
                    WHEN "Status" = 'Ошибка' THEN 'Failed'
                    ELSE 'NotStarted' END,
                "IndexCompletedChunks" = (SELECT COUNT(*) FROM "KnowledgeChunks" c WHERE c."KnowledgeDocumentId" = "KnowledgeDocuments"."Id"),
                "IndexTotalChunks" = (SELECT COUNT(*) FROM "KnowledgeChunks" c WHERE c."KnowledgeDocumentId" = "KnowledgeDocuments"."Id")
            WHERE "DocumentKind" <> 'Other'
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_KnowledgeDocuments_IndexStatus", "KnowledgeDocuments");
        migrationBuilder.DropColumn("IndexStatus", "KnowledgeDocuments");
        migrationBuilder.DropColumn("IndexTotalChunks", "KnowledgeDocuments");
        migrationBuilder.DropColumn("IndexCompletedChunks", "KnowledgeDocuments");
        migrationBuilder.DropColumn("IndexErrorCode", "KnowledgeDocuments");
        migrationBuilder.DropColumn("IndexUpdatedAt", "KnowledgeDocuments");
    }
}
