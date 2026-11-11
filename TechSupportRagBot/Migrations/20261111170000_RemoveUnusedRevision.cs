using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111170000_RemoveUnusedRevision")]
public partial class RemoveUnusedRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Revision", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Revision", "ComponentModels");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Revision", "ComponentModels", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>("Revision", "KnowledgeDocuments", type: "text", nullable: true);
    }
}
