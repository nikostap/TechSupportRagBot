using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TechSupportRagBot.Data;

#nullable disable

namespace TechSupportRagBot.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723153000_AddSecureFileStorage")]
public partial class AddSecureFileStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StorageProvider",
            table: "QAAttachments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Local");
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "QAAttachments",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()");

        migrationBuilder.AddColumn<string>(
            name: "StorageProvider",
            table: "KnowledgeDocuments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Local");
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "KnowledgeDocuments",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()");

        migrationBuilder.AddColumn<string>(
            name: "StorageProvider",
            table: "Attachments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Local");
        migrationBuilder.AddColumn<string>(
            name: "TempStorageProvider",
            table: "Attachments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PreviewStorageProvider",
            table: "Attachments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "OwnsStoredFile",
            table: "Attachments",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AvatarPublicId",
            table: "AspNetUsers",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AvatarStorageProvider",
            table: "AspNetUsers",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "Attachments"
            SET "OwnsStoredFile" = false
            WHERE replace("FilePath", chr(92), '/') LIKE 'uploads/qa/%';

            UPDATE "AspNetUsers"
            SET "AvatarPublicId" = gen_random_uuid(),
                "AvatarStorageProvider" = 'Local'
            WHERE "AvatarPath" IS NOT NULL AND btrim("AvatarPath") <> '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_PublicId",
            table: "Attachments",
            column: "PublicId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_QAAttachments_PublicId",
            table: "QAAttachments",
            column: "PublicId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeDocuments_PublicId",
            table: "KnowledgeDocuments",
            column: "PublicId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_AvatarPublicId",
            table: "AspNetUsers",
            column: "AvatarPublicId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Attachments_PublicId", "Attachments");
        migrationBuilder.DropIndex("IX_QAAttachments_PublicId", "QAAttachments");
        migrationBuilder.DropIndex("IX_KnowledgeDocuments_PublicId", "KnowledgeDocuments");
        migrationBuilder.DropIndex("IX_AspNetUsers_AvatarPublicId", "AspNetUsers");

        migrationBuilder.DropColumn("StorageProvider", "QAAttachments");
        migrationBuilder.DropColumn("PublicId", "QAAttachments");
        migrationBuilder.DropColumn("StorageProvider", "KnowledgeDocuments");
        migrationBuilder.DropColumn("PublicId", "KnowledgeDocuments");
        migrationBuilder.DropColumn("StorageProvider", "Attachments");
        migrationBuilder.DropColumn("TempStorageProvider", "Attachments");
        migrationBuilder.DropColumn("PreviewStorageProvider", "Attachments");
        migrationBuilder.DropColumn("OwnsStoredFile", "Attachments");
        migrationBuilder.DropColumn("AvatarPublicId", "AspNetUsers");
        migrationBuilder.DropColumn("AvatarStorageProvider", "AspNetUsers");
    }
}
