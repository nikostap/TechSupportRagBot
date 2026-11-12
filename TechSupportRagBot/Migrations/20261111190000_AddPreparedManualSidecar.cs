using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111190000_AddPreparedManualSidecar")]
public partial class AddPreparedManualSidecar : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PreparedFilePath", "KnowledgeDocuments", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>("PreparedFileName", "KnowledgeDocuments", type: "character varying(255)", maxLength: 255, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("PreparedFilePath", "KnowledgeDocuments");
        migrationBuilder.DropColumn("PreparedFileName", "KnowledgeDocuments");
    }
}
