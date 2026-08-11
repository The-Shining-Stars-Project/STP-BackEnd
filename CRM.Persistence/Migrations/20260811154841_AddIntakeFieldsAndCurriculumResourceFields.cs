using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeFieldsAndCurriculumResourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizationExpiry",
                table: "Participants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardianEmail",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardianName",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardianPhone",
                table: "Participants",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntakeNotes",
                table: "Participants",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralSource",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TShirtSize",
                table: "Participants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Games",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProgramId",
                table: "Games",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_ProgramId",
                table: "Games",
                column: "ProgramId");

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Programs_ProgramId",
                table: "Games",
                column: "ProgramId",
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Programs_ProgramId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_ProgramId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "AuthorizationExpiry",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "GuardianEmail",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "GuardianName",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "GuardianPhone",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "IntakeNotes",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ReferralSource",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "TShirtSize",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ProgramId",
                table: "Games");
        }
    }
}
