using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <summary>
    /// Attendance registers for productions and events, which the client asked to track
    /// separately from class attendance (Aug 2026): one combined list per event, drawing Stars
    /// from any programme or location.
    ///
    /// PURELY ADDITIVE. Three new tables and nothing else — no ALTER TABLE, no dropped or
    /// recreated index, no data backfill, and not one statement against Sessions or
    /// AttendanceRecords. That is deliberate rather than incidental: the obvious design (a Kind
    /// column on Session with a nullable ProgramId) would have required altering an indexed
    /// column and rebuilding IX_Sessions_ProgramId_Date, which is the only thing collapsing the
    /// check-then-insert race in GetOrCreateSessionAsync. It would also have hit a subtler trap
    /// — SQL Server treats NULLs as equal for unique-index purposes, so a nullable ProgramId
    /// under that index would have rejected the second event registered on any given date.
    ///
    /// Consequently this migration is safe to apply to a populated database at any time, and
    /// class attendance percentages cannot change: event attendance is a different CLR type
    /// living in a different table, so it cannot be summed with AttendanceRecord by accident.
    /// </summary>
    public partial class AddEventAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeRange = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HoursLogged = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventAttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventAttendanceRecords_EventSessions_EventSessionId",
                        column: x => x.EventSessionId,
                        principalTable: "EventSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventAttendanceRecords_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventAttendanceRecords_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventSessionSites",
                columns: table => new
                {
                    EventSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSessionSites", x => new { x.EventSessionId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_EventSessionSites_EventSessions_EventSessionId",
                        column: x => x.EventSessionId,
                        principalTable: "EventSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventSessionSites_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceRecords_EventSessionId_ParticipantId",
                table: "EventAttendanceRecords",
                columns: new[] { "EventSessionId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceRecords_ParticipantId",
                table: "EventAttendanceRecords",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceRecords_SiteId",
                table: "EventAttendanceRecords",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSessions_CalendarEventId",
                table: "EventSessions",
                column: "CalendarEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSessions_Date",
                table: "EventSessions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_EventSessionSites_SiteId",
                table: "EventSessionSites",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventAttendanceRecords");

            migrationBuilder.DropTable(
                name: "EventSessionSites");

            migrationBuilder.DropTable(
                name: "EventSessions");
        }
    }
}
