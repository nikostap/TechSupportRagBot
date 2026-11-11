using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111130000_AddComponentQa")]
public partial class AddComponentQa : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Scope", "QAEntries", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Machine");
        migrationBuilder.AddColumn<int>("ComponentModelId", "QAEntries", type: "integer", nullable: true);
        migrationBuilder.CreateIndex("IX_QAEntries_ComponentModelId", "QAEntries", "ComponentModelId");
        migrationBuilder.AddForeignKey(name: "FK_QAEntries_ComponentModels_ComponentModelId",
            table: "QAEntries", column: "ComponentModelId",
            principalTable: "ComponentModels", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_QAEntries_ComponentModels_ComponentModelId", "QAEntries");
        migrationBuilder.DropIndex("IX_QAEntries_ComponentModelId", "QAEntries");
        migrationBuilder.DropColumn("Scope", "QAEntries");
        migrationBuilder.DropColumn("ComponentModelId", "QAEntries");
    }
}
