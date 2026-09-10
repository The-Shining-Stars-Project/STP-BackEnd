using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSitesAndMultiSiteRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Sites",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RosterAssignmentSites",
                columns: table => new
                {
                    RosterAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterAssignmentSites", x => new { x.RosterAssignmentId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_RosterAssignmentSites_RosterAssignments_RosterAssignmentId",
                        column: x => x.RosterAssignmentId,
                        principalTable: "RosterAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterAssignmentSites_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignmentSites_SiteId",
                table: "RosterAssignmentSites",
                column: "SiteId");
            // Existing roster rows carried one site in SiteId; the new join must agree with
            // them so nothing on a live roster changes when this migration lands.
            migrationBuilder.Sql(
                "INSERT INTO [RosterAssignmentSites] ([RosterAssignmentId], [SiteId]) " +
                "SELECT [Id], [SiteId] FROM [RosterAssignments] WHERE [SiteId] IS NOT NULL;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterAssignmentSites");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Sites");
        }
    }
}
