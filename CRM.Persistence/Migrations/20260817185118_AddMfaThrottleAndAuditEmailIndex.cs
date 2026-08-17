using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaThrottleAndAuditEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedMfaAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedMfaAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaLockedUntil",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "MfaChallenges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserEmail_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "UserEmail", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_UserEmail_OccurredAt",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "FailedMfaAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastFailedMfaAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaLockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "MfaChallenges");
        }
    }
}
