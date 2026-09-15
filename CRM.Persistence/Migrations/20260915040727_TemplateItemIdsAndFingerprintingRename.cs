using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TemplateItemIdsAndFingerprintingRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateItemId",
                table: "OnboardingItems",
                type: "uniqueidentifier",
                nullable: true);
            // Link every issued checklist row to the template row it came from, matched by
            // section + label, so template renames can follow through from now on.
            migrationBuilder.Sql(
                "UPDATE oi SET oi.[TemplateItemId] = t.[Id] FROM [OnboardingItems] oi " +
                "JOIN [ChecklistTemplateItems] t ON LOWER(LTRIM(RTRIM(oi.[Section]))) = LOWER(LTRIM(RTRIM(t.[Section]))) " +
                "AND LOWER(LTRIM(RTRIM(oi.[Label]))) = LOWER(LTRIM(RTRIM(t.[Label]))) " +
                "WHERE oi.[TemplateItemId] IS NULL;");

            // Client ask 2026-09-14: "Fingerprinting & TB Clearances (Schools)" is just
            // "Fingerprinting (Schools)" — TB Clearance is its own item. Fingerprinting does
            // not renew, so the 48-month interval the label pattern gave it comes off, and an
            // expiry that interval stamped is cleared (a hand-entered one is left alone).
            migrationBuilder.Sql(
                "UPDATE [OnboardingItems] SET [ExpiryDate] = NULL " +
                "WHERE [Label] = 'Fingerprinting & TB Clearances (Schools)' AND [RenewalMonths] = 48 " +
                "AND [CompletedDate] IS NOT NULL AND [ExpiryDate] = DATEADD(month, 48, [CompletedDate]);");
            migrationBuilder.Sql(
                "UPDATE [OnboardingItems] SET [Label] = 'Fingerprinting (Schools)', [RenewalMonths] = NULL " +
                "WHERE [Label] = 'Fingerprinting & TB Clearances (Schools)';");
            migrationBuilder.Sql(
                "UPDATE [ChecklistTemplateItems] SET [Label] = 'Fingerprinting (Schools)', [RenewalMonths] = NULL " +
                "WHERE [Label] = 'Fingerprinting & TB Clearances (Schools)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemplateItemId",
                table: "OnboardingItems");
        }
    }
}
