using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021115000_ApproveOwnerConfirmedQaEntries")]
public partial class ApproveOwnerConfirmedQaEntries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE "QAEntries" AS qa
        SET "AvailableToClients" = TRUE
        FROM (VALUES
            (45, '06e7685c051f220ac213295024919bbf'),
            (98, '762939bf5150abbe077860c1f026e1e6'),
            (159, '7e47c87eaabf2916e5a70d7a0daf152a'),
            (160, 'ff77096ea95432a05bf61504e693a6f1')
        ) AS confirmed(id, digest)
        WHERE qa."Id" = confirmed.id
          AND qa."Status" = 'Verified'
          AND md5(qa."Question" || chr(31) || qa."Answer") = confirmed.digest;
        """);
}
