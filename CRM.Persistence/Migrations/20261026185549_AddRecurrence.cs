using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceParentId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepeatAnchor",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatFrequency",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepeatInterval",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepeatUntil",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatWeekdays",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepeatFrequency",
                table: "CalendarEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepeatInterval",
                table: "CalendarEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepeatUntil",
                table: "CalendarEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatWeekdays",
                table: "CalendarEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CalendarEventSkips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEventSkips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarEventSkips_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RecurrenceParentId",
                table: "Tasks",
                column: "RecurrenceParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEventSkips_CalendarEventId_Date",
                table: "CalendarEventSkips",
                columns: new[] { "CalendarEventId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEventSkips");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RecurrenceParentId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceParentId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatAnchor",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatFrequency",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatInterval",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatUntil",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatWeekdays",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RepeatFrequency",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RepeatInterval",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RepeatUntil",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RepeatWeekdays",
                table: "CalendarEvents");
        }
    }
}
