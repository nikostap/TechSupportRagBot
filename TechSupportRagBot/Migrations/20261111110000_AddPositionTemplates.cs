using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111110000_AddPositionTemplates")]
public partial class AddPositionTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PositionTemplates", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            ComponentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_PositionTemplates", x => x.Id));
        migrationBuilder.CreateIndex("IX_PositionTemplates_Name_ComponentType", "PositionTemplates", new[] { "Name", "ComponentType" }, unique: true);
        migrationBuilder.AddColumn<int>("PositionTemplateId", "MachinePositions", type: "integer", nullable: true);
        migrationBuilder.CreateIndex("IX_MachinePositions_PositionTemplateId", "MachinePositions", "PositionTemplateId");
        migrationBuilder.AddForeignKey(name: "FK_MachinePositions_PositionTemplates_PositionTemplateId",
            table: "MachinePositions", column: "PositionTemplateId",
            principalTable: "PositionTemplates", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_MachinePositions_PositionTemplates_PositionTemplateId", "MachinePositions");
        migrationBuilder.DropIndex("IX_MachinePositions_PositionTemplateId", "MachinePositions");
        migrationBuilder.DropColumn("PositionTemplateId", "MachinePositions");
        migrationBuilder.DropTable("PositionTemplates");
    }
}
