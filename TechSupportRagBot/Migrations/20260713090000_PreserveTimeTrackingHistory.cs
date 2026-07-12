using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260713090000_PreserveTimeTrackingHistory")]
public partial class PreserveTimeTrackingHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("OperatorName", "OperatorChatTimeEntries", "character varying(256)", maxLength: 256, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("MachineModel", "OperatorChatTimeEntries", "character varying(100)", maxLength: 100, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("TicketReference", "OperatorChatTimeEntries", "character varying(300)", maxLength: 300, nullable: false, defaultValue: "");
        migrationBuilder.Sql("""
            UPDATE "OperatorChatTimeEntries" e
            SET "OperatorName" = COALESCE(u."FullName", u."UserName", e."OperatorUserId"),
                "MachineModel" = COALESCE(m."Model", ''),
                "TicketReference" = CONCAT('#', t."Id", ' ', t."Title")
            FROM "AspNetUsers" u, "Machines" m, "Tickets" t
            WHERE u."Id" = e."OperatorUserId" AND m."Id" = e."MachineId" AND t."Id" = e."TicketId";
            """);
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_AspNetUsers_OperatorUserId", "OperatorChatTimeEntries");
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_Machines_MachineId", "OperatorChatTimeEntries");
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_Tickets_TicketId", "OperatorChatTimeEntries");
        migrationBuilder.AlterColumn<string>("OperatorUserId", "OperatorChatTimeEntries", "text", nullable: true, oldClrType: typeof(string), oldType: "text");
        migrationBuilder.AlterColumn<int>("MachineId", "OperatorChatTimeEntries", "integer", nullable: true, oldClrType: typeof(int), oldType: "integer");
        migrationBuilder.AlterColumn<int>("TicketId", "OperatorChatTimeEntries", "integer", nullable: true, oldClrType: typeof(int), oldType: "integer");
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_AspNetUsers_OperatorUserId", "OperatorChatTimeEntries", "OperatorUserId", "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_Machines_MachineId", "OperatorChatTimeEntries", "MachineId", "Machines", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_Tickets_TicketId", "OperatorChatTimeEntries", "TicketId", "Tickets", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_AspNetUsers_OperatorUserId", "OperatorChatTimeEntries");
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_Machines_MachineId", "OperatorChatTimeEntries");
        migrationBuilder.DropForeignKey("FK_OperatorChatTimeEntries_Tickets_TicketId", "OperatorChatTimeEntries");
        migrationBuilder.Sql("DELETE FROM \"OperatorChatTimeEntries\" WHERE \"OperatorUserId\" IS NULL OR \"MachineId\" IS NULL OR \"TicketId\" IS NULL;");
        migrationBuilder.AlterColumn<string>("OperatorUserId", "OperatorChatTimeEntries", "text", nullable: false, oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.AlterColumn<int>("MachineId", "OperatorChatTimeEntries", "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AlterColumn<int>("TicketId", "OperatorChatTimeEntries", "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_AspNetUsers_OperatorUserId", "OperatorChatTimeEntries", "OperatorUserId", "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_Machines_MachineId", "OperatorChatTimeEntries", "MachineId", "Machines", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_OperatorChatTimeEntries_Tickets_TicketId", "OperatorChatTimeEntries", "TicketId", "Tickets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.DropColumn("OperatorName", "OperatorChatTimeEntries");
        migrationBuilder.DropColumn("MachineModel", "OperatorChatTimeEntries");
        migrationBuilder.DropColumn("TicketReference", "OperatorChatTimeEntries");
    }
}
