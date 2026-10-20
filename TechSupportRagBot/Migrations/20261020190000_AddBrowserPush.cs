using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020190000_AddBrowserPush")]
public partial class AddBrowserPush : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BrowserPushSubscriptions",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(nullable: false),
                Endpoint = table.Column<string>(maxLength: 2048, nullable: false),
                P256dh = table.Column<string>(nullable: false),
                Auth = table.Column<string>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                LastSeenAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BrowserPushSubscriptions", x => x.Id);
                table.ForeignKey("FK_BrowserPushSubscriptions_AspNetUsers_UserId", x => x.UserId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateTable(
            name: "BrowserPushLogs",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SubscriptionId = table.Column<int>(nullable: false),
                EventKey = table.Column<string>(maxLength: 120, nullable: false),
                SentAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BrowserPushLogs", x => x.Id);
                table.ForeignKey("FK_BrowserPushLogs_BrowserPushSubscriptions_SubscriptionId", x => x.SubscriptionId,
                    "BrowserPushSubscriptions", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_BrowserPushSubscriptions_UserId", "BrowserPushSubscriptions", "UserId");
        migrationBuilder.CreateIndex("IX_BrowserPushSubscriptions_Endpoint", "BrowserPushSubscriptions", "Endpoint", unique: true);
        migrationBuilder.CreateIndex("IX_BrowserPushLogs_SubscriptionId_EventKey", "BrowserPushLogs",
            new[] { "SubscriptionId", "EventKey" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("BrowserPushLogs");
        migrationBuilder.DropTable("BrowserPushSubscriptions");
    }
}
