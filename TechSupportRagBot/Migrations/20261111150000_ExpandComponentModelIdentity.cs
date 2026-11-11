using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261111150000_ExpandComponentModelIdentity")]
public partial class ExpandComponentModelIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_ComponentModels_Manufacturer_PartNumber_Revision", "ComponentModels");
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_ComponentModels_Identity" ON "ComponentModels"
            (lower(btrim("Type")), lower(btrim("Manufacturer")), lower(btrim("Series")),
             lower(btrim("PartNumber")), lower(btrim(coalesce("Rating", ''))))
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_ComponentModels_Identity", "ComponentModels");
        // New rows may have no part number; a non-unique fallback keeps rollback possible.
        migrationBuilder.CreateIndex("IX_ComponentModels_Manufacturer_PartNumber_Revision",
            "ComponentModels", new[] { "Manufacturer", "PartNumber", "Revision" });
    }
}
