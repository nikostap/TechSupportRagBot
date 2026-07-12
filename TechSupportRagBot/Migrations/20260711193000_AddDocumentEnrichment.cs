using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711193000_AddDocumentEnrichment")]
public partial class AddDocumentEnrichment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("DetectedDocumentType", "KnowledgeDocuments", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<DateTime>("EnrichedAt", "KnowledgeDocuments", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("EnrichmentJson", "KnowledgeDocuments", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>("EnrichmentMode", "KnowledgeDocuments", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Manual");
        migrationBuilder.AddColumn<string>("EnrichmentModel", "KnowledgeDocuments", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("NodeName", "KnowledgeDocuments", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>("Summary", "KnowledgeDocuments", type: "character varying(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>("Tags", "KnowledgeDocuments", type: "character varying(3000)", maxLength: 3000, nullable: true);
        migrationBuilder.AddColumn<string>("Title", "KnowledgeDocuments", type: "character varying(300)", maxLength: 300, nullable: true);

        migrationBuilder.AddColumn<string>("Operations", "KnowledgeChunks", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("SearchQuestions", "KnowledgeChunks", type: "character varying(3000)", maxLength: 3000, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("DetectedDocumentType", "KnowledgeDocuments");
        migrationBuilder.DropColumn("EnrichedAt", "KnowledgeDocuments");
        migrationBuilder.DropColumn("EnrichmentJson", "KnowledgeDocuments");
        migrationBuilder.DropColumn("EnrichmentMode", "KnowledgeDocuments");
        migrationBuilder.DropColumn("EnrichmentModel", "KnowledgeDocuments");
        migrationBuilder.DropColumn("NodeName", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Summary", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Tags", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Title", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Operations", "KnowledgeChunks");
        migrationBuilder.DropColumn("SearchQuestions", "KnowledgeChunks");
    }
}
