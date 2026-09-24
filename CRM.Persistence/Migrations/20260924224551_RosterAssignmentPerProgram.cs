using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RosterAssignmentPerProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RosterAssignments_ParticipantId_Year_Quarter",
                table: "RosterAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "ProgramId",
                table: "RosterAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing placements were one-per-star; they belong to the star's primary program.
            migrationBuilder.Sql(
                "UPDATE r SET r.[ProgramId] = p.[ProgramId] FROM [RosterAssignments] r " +
                "JOIN [Participants] p ON p.[Id] = r.[ParticipantId];");

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignments_ParticipantId_ProgramId_Year_Quarter",
                table: "RosterAssignments",
                columns: new[] { "ParticipantId", "ProgramId", "Year", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignments_ProgramId",
                table: "RosterAssignments",
                column: "ProgramId");

            migrationBuilder.AddForeignKey(
                name: "FK_RosterAssignments_Programs_ProgramId",
                table: "RosterAssignments",
                column: "ProgramId",
                principalTable: "Programs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterAssignments_Programs_ProgramId",
                table: "RosterAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RosterAssignments_ParticipantId_ProgramId_Year_Quarter",
                table: "RosterAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RosterAssignments_ProgramId",
                table: "RosterAssignments");

            migrationBuilder.DropColumn(
                name: "ProgramId",
                table: "RosterAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignments_ParticipantId_Year_Quarter",
                table: "RosterAssignments",
                columns: new[] { "ParticipantId", "Year", "Quarter" },
                unique: true);
        }
    }
}
