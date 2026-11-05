using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105120000_AddClientMonthlyPasswordsAndRenewal")]
public partial class AddClientMonthlyPasswordsAndRenewal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("MonthlyPasswordEmailEnabled", "Clients", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("MonthlyPasswordSendDay", "Clients", type: "integer", nullable: false, defaultValue: 28);
        migrationBuilder.AddColumn<bool>("AutoRenewSupportLicense", "Clients", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.CreateTable(
            name: "MonthlyPasswordEmailLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClientId = table.Column<int>(type: "integer", nullable: false),
                Year = table.Column<int>(type: "integer", nullable: false),
                Month = table.Column<int>(type: "integer", nullable: false),
                RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MonthlyPasswordEmailLogs", x => x.Id);
                table.ForeignKey("FK_MonthlyPasswordEmailLogs_Clients_ClientId", x => x.ClientId,
                    "Clients", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_MonthlyPasswordEmailLogs_ClientId_Year_Month",
            "MonthlyPasswordEmailLogs", new[] { "ClientId", "Year", "Month" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MonthlyPasswordEmailLogs");
        migrationBuilder.DropColumn("MonthlyPasswordEmailEnabled", "Clients");
        migrationBuilder.DropColumn("MonthlyPasswordSendDay", "Clients");
        migrationBuilder.DropColumn("AutoRenewSupportLicense", "Clients");
    }
}
