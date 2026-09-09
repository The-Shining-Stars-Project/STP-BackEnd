using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfBlobName",
                table: "Scripts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfFileName",
                table: "Scripts",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PdfSizeBytes",
                table: "Scripts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PdfUploadedAt",
                table: "Scripts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfBlobName",
                table: "Scripts");

            migrationBuilder.DropColumn(
                name: "PdfFileName",
                table: "Scripts");

            migrationBuilder.DropColumn(
                name: "PdfSizeBytes",
                table: "Scripts");

            migrationBuilder.DropColumn(
                name: "PdfUploadedAt",
                table: "Scripts");
        }
    }
}
