using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Track",
                table: "Programs",
                type: "int",
                nullable: false,
                defaultValue: 0);
            // The client's programs were created by hand, so nothing else knows which are
            // Pathways. Infer once from the name (e.g. "Pathways: Manteca"); the Programs
            // page owns the setting from here on.
            migrationBuilder.Sql(
                "UPDATE [Programs] SET [Track] = 1 WHERE LOWER([Name]) LIKE '%pathways%' OR LOWER([Slug]) LIKE '%pathways%';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Track",
                table: "Programs");
        }
    }
}
