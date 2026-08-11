using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeExpansionPathwaysTrackDualEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TShirtSize",
                table: "Staff",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Allergies",
                table: "Participants",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllergyAnaphylactic",
                table: "Participants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AreasOfConcern",
                table: "Participants",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactInRemind",
                table: "Participants",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Participants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasHighSchoolDiploma",
                table: "Participants",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IntakeDocsSubmitted",
                table: "Participants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "IppExpiry",
                table: "Participants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecondaryProgramId",
                table: "Participants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceCoordinatorEmail",
                table: "Participants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceCoordinatorPhone",
                table: "Participants",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnnualGoal",
                table: "ObjectiveAreas",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SixMonthBenchmark",
                table: "ObjectiveAreas",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Track",
                table: "ObjectiveAreas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_SecondaryProgramId",
                table: "Participants",
                column: "SecondaryProgramId");

            migrationBuilder.AddForeignKey(
                name: "FK_Participants_Programs_SecondaryProgramId",
                table: "Participants",
                column: "SecondaryProgramId",
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Participants_Programs_SecondaryProgramId",
                table: "Participants");

            migrationBuilder.DropIndex(
                name: "IX_Participants_SecondaryProgramId",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "TShirtSize",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "Allergies",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "AllergyAnaphylactic",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "AreasOfConcern",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ContactInRemind",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "HasHighSchoolDiploma",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "IntakeDocsSubmitted",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "IppExpiry",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SecondaryProgramId",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ServiceCoordinatorEmail",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ServiceCoordinatorPhone",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "AnnualGoal",
                table: "ObjectiveAreas");

            migrationBuilder.DropColumn(
                name: "SixMonthBenchmark",
                table: "ObjectiveAreas");

            migrationBuilder.DropColumn(
                name: "Track",
                table: "ObjectiveAreas");
        }
    }
}
