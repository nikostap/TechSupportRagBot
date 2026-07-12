using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260712153000_AddWorkCalendar")]
public partial class AddWorkCalendar : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WorkCalendarDays",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                DayType = table.Column<int>(type: "integer", nullable: false),
                Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WorkCalendarDays", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_WorkCalendarDays_Date",
            table: "WorkCalendarDays",
            column: "Date",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "WorkCalendarDays");
}
