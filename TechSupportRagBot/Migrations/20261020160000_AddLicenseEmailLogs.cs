using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020160000_AddLicenseEmailLogs")]
public partial class AddLicenseEmailLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LicenseEmailLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                LicenseId = table.Column<int>(type: "integer", nullable: false),
                Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LicenseEmailLogs", x => x.Id);
                table.ForeignKey("FK_LicenseEmailLogs_Licenses_LicenseId", x => x.LicenseId,
                    "Licenses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LicenseEmailLogs_LicenseId_Kind_RecipientEmail",
            table: "LicenseEmailLogs",
            columns: new[] { "LicenseId", "Kind", "RecipientEmail" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LicenseEmailLogs");
}
