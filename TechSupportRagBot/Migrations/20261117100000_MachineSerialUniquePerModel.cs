using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117100000_MachineSerialUniquePerModel")]
public partial class MachineSerialUniquePerModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Machines_SerialNumber", "Machines");
        migrationBuilder.CreateIndex("IX_Machines_Model_SerialNumber", "Machines",
            new[] { "Model", "SerialNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Machines_Model_SerialNumber", "Machines");
        migrationBuilder.CreateIndex("IX_Machines_SerialNumber", "Machines", "SerialNumber", unique: true);
    }
}
