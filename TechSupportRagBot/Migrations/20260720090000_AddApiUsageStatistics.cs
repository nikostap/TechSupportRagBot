using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TechSupportRagBot.Data;

#nullable disable
namespace TechSupportRagBot.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720090000_AddApiUsageStatistics")]
public partial class AddApiUsageStatistics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name:"ApiUsageRecords", columns: table => new {
            Id = table.Column<long>(type:"bigint", nullable:false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            CreatedAt = table.Column<DateTime>(type:"timestamp with time zone", nullable:false), Provider = table.Column<string>(type:"character varying(40)", maxLength:40, nullable:false),
            Model = table.Column<string>(type:"character varying(120)", maxLength:120, nullable:false), Category = table.Column<string>(type:"character varying(40)", maxLength:40, nullable:false),
            Operation = table.Column<string>(type:"character varying(40)", maxLength:40, nullable:false), InputTokens = table.Column<int>(type:"integer", nullable:false), OutputTokens = table.Column<int>(type:"integer", nullable:false),
            EstimatedCostRub = table.Column<decimal>(type:"numeric(18,6)", precision:18, scale:6, nullable:false)
        }, constraints: table => table.PrimaryKey("PK_ApiUsageRecords", x => x.Id));
        migrationBuilder.CreateIndex("IX_ApiUsageRecords_CreatedAt", "ApiUsageRecords", "CreatedAt");
        migrationBuilder.CreateIndex("IX_ApiUsageRecords_Category_CreatedAt", "ApiUsageRecords", new[]{"Category","CreatedAt"});
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ApiUsageRecords");
}
