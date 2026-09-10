using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "OnboardingItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "OnboardingItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "OnboardingItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "OnboardingItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAt",
                table: "OnboardingItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "DocumentRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "DocumentRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DocumentRecords",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "DocumentRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAt",
                table: "DocumentRecords",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "DocumentRecords");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "DocumentRecords");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DocumentRecords");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "DocumentRecords");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "DocumentRecords");
        }
    }
}
