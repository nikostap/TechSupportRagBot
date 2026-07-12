using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260712113000_AddStructuredChatArchives")]
public partial class AddStructuredChatArchives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Title", "ResolvedAnswers", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>("AlternativeQuestions", "ResolvedAnswers", type: "character varying(3000)", maxLength: 3000, nullable: true);
        migrationBuilder.AddColumn<string>("Tags", "ResolvedAnswers", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("NodeName", "ResolvedAnswers", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ProblemType", "ResolvedAnswers", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<double>("Confidence", "ResolvedAnswers", type: "double precision", nullable: false, defaultValue: 0.0);
        migrationBuilder.AddColumn<string>("Status", "ResolvedAnswers", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Indexed");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Title", "ResolvedAnswers");
        migrationBuilder.DropColumn("AlternativeQuestions", "ResolvedAnswers");
        migrationBuilder.DropColumn("Tags", "ResolvedAnswers");
        migrationBuilder.DropColumn("NodeName", "ResolvedAnswers");
        migrationBuilder.DropColumn("ProblemType", "ResolvedAnswers");
        migrationBuilder.DropColumn("Confidence", "ResolvedAnswers");
        migrationBuilder.DropColumn("Status", "ResolvedAnswers");
    }
}
