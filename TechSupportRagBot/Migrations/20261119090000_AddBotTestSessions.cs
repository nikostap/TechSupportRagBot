using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119090000_AddBotTestSessions")]
public partial class AddBotTestSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BotTestSessions",
            columns: table => new
            {
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                MachineId = table.Column<int>(nullable: false),
                Language = table.Column<string>(maxLength: 40, nullable: false),
                HistoryJson = table.Column<string>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_BotTestSessions", x => x.UserId));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("BotTestSessions");
    }
}
