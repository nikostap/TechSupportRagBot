using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261112120000_AddKnowledgeDocumentTargets")]
public partial class AddKnowledgeDocumentTargets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("KnowledgeDocumentTargets", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            KnowledgeDocumentId = table.Column<int>(type: "integer", nullable: false),
            MachineModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
            MachineId = table.Column<int>(type: "integer", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_KnowledgeDocumentTargets", x => x.Id);
            table.ForeignKey("FK_KnowledgeDocumentTargets_KnowledgeDocuments_KnowledgeDocumentId", x => x.KnowledgeDocumentId,
                "KnowledgeDocuments", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_KnowledgeDocumentTargets_Machines_MachineId", x => x.MachineId,
                "Machines", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_KnowledgeDocumentTargets_OneTarget",
                "(\"MachineModel\" IS NOT NULL AND \"MachineId\" IS NULL) OR (\"MachineModel\" IS NULL AND \"MachineId\" IS NOT NULL)");
        });
        migrationBuilder.CreateIndex("IX_KnowledgeDocumentTargets_KnowledgeDocumentId_MachineModel", "KnowledgeDocumentTargets",
            new[] { "KnowledgeDocumentId", "MachineModel" });
        migrationBuilder.CreateIndex("IX_KnowledgeDocumentTargets_KnowledgeDocumentId_MachineId", "KnowledgeDocumentTargets",
            new[] { "KnowledgeDocumentId", "MachineId" });
        migrationBuilder.CreateIndex("IX_KnowledgeDocumentTargets_MachineId", "KnowledgeDocumentTargets", "MachineId");
        migrationBuilder.Sql("""
            INSERT INTO "KnowledgeDocumentTargets" ("KnowledgeDocumentId", "MachineModel", "MachineId")
            SELECT "Id", NULL, "MachineId" FROM "KnowledgeDocuments" WHERE "MachineId" IS NOT NULL;
            INSERT INTO "KnowledgeDocumentTargets" ("KnowledgeDocumentId", "MachineModel", "MachineId")
            SELECT "Id", "MachineModel", NULL FROM "KnowledgeDocuments"
            WHERE "MachineId" IS NULL AND NULLIF("MachineModel", '') IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("KnowledgeDocumentTargets");
}
