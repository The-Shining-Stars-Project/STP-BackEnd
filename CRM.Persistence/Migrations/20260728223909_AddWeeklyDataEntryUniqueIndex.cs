using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <summary>
    /// Enforces one weekly score per Star / sub-skill / week (#6).
    ///
    /// RecordWeeklyScoreAsync upserts on this tuple with a check-then-insert and the index
    /// was never unique, so concurrent or double-tapped submissions could write duplicate
    /// rows — which ComputeMonthEndAsync then averaged, skewing month-end progress levels.
    ///
    /// Any duplicates already in the table are collapsed before the constraint goes on,
    /// keeping the most recently updated row (which matches the upsert's last-write-wins
    /// semantics). That deletion is not reversible by Down().
    /// </summary>
    public partial class AddWeeklyDataEntryUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeeklyDataEntries_ParticipantId_SubSkillId_MonthKey_WeekNumber",
                table: "WeeklyDataEntries");

            // Collapse pre-existing duplicates, newest kept, before the unique index is
            // created — otherwise CREATE UNIQUE INDEX fails and the deploy stops here.
            migrationBuilder.Sql("""
                WITH Ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY [ParticipantId], [SubSkillId], [MonthKey], [WeekNumber]
                               ORDER BY [UpdatedAt] DESC, [CreatedAt] DESC, [Id] DESC) AS RowRank
                    FROM [WeeklyDataEntries]
                )
                DELETE FROM [WeeklyDataEntries]
                WHERE [Id] IN (SELECT [Id] FROM Ranked WHERE RowRank > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyDataEntries_Participant_SubSkill_Month_Week",
                table: "WeeklyDataEntries",
                columns: new[] { "ParticipantId", "SubSkillId", "MonthKey", "WeekNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeeklyDataEntries_Participant_SubSkill_Month_Week",
                table: "WeeklyDataEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyDataEntries_ParticipantId_SubSkillId_MonthKey_WeekNumber",
                table: "WeeklyDataEntries",
                columns: new[] { "ParticipantId", "SubSkillId", "MonthKey", "WeekNumber" });
        }
    }
}
