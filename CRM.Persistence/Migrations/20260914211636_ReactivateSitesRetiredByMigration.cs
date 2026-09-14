using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReactivateSitesRetiredByMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddSitesAndMultiSiteRoster (Sep 10) added Sites.IsActive with a default of FALSE
            // and never set it for the rows that already existed, so every site the org had
            // silently became "retired": gone from the Roster chips, the event pickers and
            // (because Settings hides retired sites) from Settings too. Re-activate anything
            // that has not been touched since — a site someone deliberately retired through
            // the UI after the 11th carries a later UpdatedAt and is left alone.
            migrationBuilder.Sql(
                "UPDATE [Sites] SET [IsActive] = 1 WHERE [IsActive] = 0 AND [UpdatedAt] < '2026-09-11T00:00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
