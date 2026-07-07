using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations
{
    [DbContextAttribute(typeof(ApplicationDbContext))]
    [Migration("20260707162102_AddAccessProfileToUsers")]
    public partial class AddAccessProfileToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessProfile",
                table: "AspNetUsers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessProfile",
                table: "AspNetUsers");
        }
    }
}
