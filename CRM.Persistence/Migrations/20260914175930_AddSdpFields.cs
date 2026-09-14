using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSdpFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSdpClient",
                table: "Participants",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SdpFmsName",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SdpIndependentFacilitator",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SdpStartDate",
                table: "Participants",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSdpClient",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SdpFmsName",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SdpIndependentFacilitator",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SdpStartDate",
                table: "Participants");
        }
    }
}
