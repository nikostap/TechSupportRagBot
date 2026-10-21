using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021112000_AddOperatorReadReceipts")]
public partial class AddOperatorReadReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChatMessageOperatorReads",
            columns: table => new
            {
                ChatMessageId = table.Column<int>(type: "integer", nullable: false),
                OperatorUserId = table.Column<string>(type: "text", nullable: false),
                ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessageOperatorReads", x => new { x.ChatMessageId, x.OperatorUserId });
                table.ForeignKey("FK_ChatMessageOperatorReads_ChatMessages_ChatMessageId", x => x.ChatMessageId,
                    "ChatMessages", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_ChatMessageOperatorReads_AspNetUsers_OperatorUserId", x => x.OperatorUserId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_ChatMessageOperatorReads_OperatorUserId",
            table: "ChatMessageOperatorReads", column: "OperatorUserId");
        migrationBuilder.Sql("""
            INSERT INTO "ChatMessageOperatorReads" ("ChatMessageId", "OperatorUserId", "ReadAt")
            SELECT DISTINCT m."Id", a."OperatorUserId", CURRENT_TIMESTAMP
            FROM "ChatMessages" m
            JOIN "TicketOperatorAssignments" a ON a."TicketId" = m."TicketId"
            WHERE m."IsReadByOperator" = TRUE
            ON CONFLICT DO NOTHING;
            """);
        migrationBuilder.Sql("""
            INSERT INTO "ChatMessageOperatorReads" ("ChatMessageId", "OperatorUserId", "ReadAt")
            SELECT m."Id", t."OperatorUserId", CURRENT_TIMESTAMP
            FROM "ChatMessages" m
            JOIN "Tickets" t ON t."Id" = m."TicketId"
            WHERE m."IsReadByOperator" = TRUE AND t."OperatorUserId" IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ChatMessageOperatorReads");
}
