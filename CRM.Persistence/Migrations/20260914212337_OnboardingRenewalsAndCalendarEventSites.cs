using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingRenewalsAndCalendarEventSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNotApplicable",
                table: "OnboardingItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RenewalMonths",
                table: "OnboardingItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalMonths",
                table: "ChecklistTemplateItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalendarEventSites",
                columns: table => new
                {
                    CalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEventSites", x => new { x.CalendarEventId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_CalendarEventSites_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalendarEventSites_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEventSites_SiteId",
                table: "CalendarEventSites",
                column: "SiteId");
            // Renewal intervals the client stated on 2026-09-14: TB every 4 years, CPR and
            // harassment training every 2. Applied by label to the template AND to every
            // checklist already issued, so current staff get the same rule. Fingerprinting is
            // deliberately not touched here: the client wants it as its own item, which they
            // add in the checklist editor (the editor now syncs to current staff).
            foreach (var (pattern, months) in new[] { ("%TB %", 48), ("%TB test%", 48), ("%tuberculosis%", 48), ("%CPR%", 24), ("%harass%", 24) })
            {
                migrationBuilder.Sql($"UPDATE [ChecklistTemplateItems] SET [RenewalMonths] = {months} WHERE [RenewalMonths] IS NULL AND [Label] LIKE '{pattern}';");
                migrationBuilder.Sql($"UPDATE [OnboardingItems] SET [RenewalMonths] = {months} WHERE [RenewalMonths] IS NULL AND [Label] LIKE '{pattern}';");
            }
            // Items already completed get their expiry stamped from the completion date.
            migrationBuilder.Sql(
                "UPDATE [OnboardingItems] SET [ExpiryDate] = DATEADD(month, [RenewalMonths], [CompletedDate]) " +
                "WHERE [RenewalMonths] IS NOT NULL AND [IsCompleted] = 1 AND [CompletedDate] IS NOT NULL AND [ExpiryDate] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEventSites");

            migrationBuilder.DropColumn(
                name: "IsNotApplicable",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "RenewalMonths",
                table: "OnboardingItems");

            migrationBuilder.DropColumn(
                name: "RenewalMonths",
                table: "ChecklistTemplateItems");
        }
    }
}
