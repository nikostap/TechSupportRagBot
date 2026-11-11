using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111100000_AddComponentKnowledge")]
public partial class AddComponentKnowledge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ComponentModels", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
            Manufacturer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
            Series = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
            PartNumber = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
            Rating = table.Column<string>(type: "text", nullable: true),
            Revision = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
            IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_ComponentModels", x => x.Id));

        migrationBuilder.CreateTable("MachinePositions", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            MachineModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
            Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            HmiLabel = table.Column<string>(type: "text", nullable: true),
            SchemeLabel = table.Column<string>(type: "text", nullable: true),
            ComponentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_MachinePositions", x => x.Id));

        migrationBuilder.CreateTable("ComponentInstallations", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            MachinePositionId = table.Column<int>(type: "integer", nullable: false),
            ComponentModelId = table.Column<int>(type: "integer", nullable: false),
            MachineId = table.Column<int>(type: "integer", nullable: true),
            SerialFrom = table.Column<int>(type: "integer", nullable: true),
            SerialTo = table.Column<int>(type: "integer", nullable: true),
            IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ComponentInstallations", x => x.Id);
            table.ForeignKey("FK_ComponentInstallations_MachinePositions_MachinePositionId", x => x.MachinePositionId, "MachinePositions", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ComponentInstallations_ComponentModels_ComponentModelId", x => x.ComponentModelId, "ComponentModels", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ComponentInstallations_Machines_MachineId", x => x.MachineId, "Machines", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.AddColumn<string>("DocumentKind", "KnowledgeDocuments", type: "text", nullable: false, defaultValue: "Other");
        migrationBuilder.AddColumn<string>("Revision", "KnowledgeDocuments", type: "text", nullable: true);
        migrationBuilder.AddColumn<int>("ComponentModelId", "KnowledgeDocuments", type: "integer", nullable: true);
        migrationBuilder.AddColumn<bool>("ClientDownloadAllowed", "KnowledgeDocuments", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("ShowInClientLibrary", "KnowledgeDocuments", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex("IX_ComponentModels_Manufacturer_PartNumber_Revision", "ComponentModels", new[] { "Manufacturer", "PartNumber", "Revision" }, unique: true);
        migrationBuilder.CreateIndex("IX_MachinePositions_MachineModel_Key", "MachinePositions", new[] { "MachineModel", "Key" }, unique: true);
        migrationBuilder.CreateIndex("IX_ComponentInstallations_MachinePositionId_MachineId", "ComponentInstallations", new[] { "MachinePositionId", "MachineId" });
        migrationBuilder.CreateIndex("IX_ComponentInstallations_ComponentModelId", "ComponentInstallations", "ComponentModelId");
        migrationBuilder.CreateIndex("IX_ComponentInstallations_MachineId", "ComponentInstallations", "MachineId");
        migrationBuilder.CreateIndex("IX_KnowledgeDocuments_ComponentModelId", "KnowledgeDocuments", "ComponentModelId");
        migrationBuilder.AddForeignKey(
            name: "FK_KnowledgeDocuments_ComponentModels_ComponentModelId",
            table: "KnowledgeDocuments", column: "ComponentModelId",
            principalTable: "ComponentModels", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_KnowledgeDocuments_ComponentModels_ComponentModelId", "KnowledgeDocuments");
        migrationBuilder.DropIndex("IX_KnowledgeDocuments_ComponentModelId", "KnowledgeDocuments");
        migrationBuilder.DropColumn("DocumentKind", "KnowledgeDocuments");
        migrationBuilder.DropColumn("Revision", "KnowledgeDocuments");
        migrationBuilder.DropColumn("ComponentModelId", "KnowledgeDocuments");
        migrationBuilder.DropColumn("ClientDownloadAllowed", "KnowledgeDocuments");
        migrationBuilder.DropColumn("ShowInClientLibrary", "KnowledgeDocuments");
        migrationBuilder.DropTable("ComponentInstallations");
        migrationBuilder.DropTable("MachinePositions");
        migrationBuilder.DropTable("ComponentModels");
    }
}
