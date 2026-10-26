using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRoleAndCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CompletionEstimated",
                table: "Tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Tasks",
                type: "int",
                nullable: true);

            // Backfill (Oct 2026, agreed with Isaiah): tasks already Done when this ships have no
            // recorded completer. Credit each one to whoever made its most recent successful
            // task.update in the audit log — usually the person who checked it off, but someone
            // could have edited it afterwards, so the row is flagged CompletionEstimated and the
            // report labels it. OccurredAt is UTC; CompletedAt is org-local (Pacific), matching
            // IOrgClock.Now, so the month boundaries line up. Status 2 = TaskStatus.Done.
            // Done tasks with no audit row (seeded, or done before auditing) stay null.
            migrationBuilder.Sql(@"
UPDATE t
SET t.[CompletedByUserId]   = a.[UserId],
    t.[CompletedAt]         = CAST((a.[OccurredAt] AT TIME ZONE 'UTC') AT TIME ZONE 'Pacific Standard Time' AS datetime2),
    t.[CompletionEstimated] = 1
FROM [Tasks] t
CROSS APPLY (
    SELECT TOP 1 e.[UserId], e.[OccurredAt]
    FROM [AuditEvents] e
    WHERE e.[Action] = 'task.update'
      AND e.[EntityId] = t.[Id]
      AND e.[Succeeded] = 1
      AND e.[UserId] IS NOT NULL
    ORDER BY e.[OccurredAt] DESC
) a
WHERE t.[Status] = 2 AND t.[CompletedByUserId] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompletedAt",
                table: "Tasks",
                column: "CompletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_CompletedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CompletionEstimated",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Tasks");
        }
    }
}
