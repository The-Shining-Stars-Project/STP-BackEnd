using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpStep",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaEnabledAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfaChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_TokenHash",
                table: "MfaChallenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_UserId_ConsumedAt",
                table: "MfaChallenges",
                columns: new[] { "UserId", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_CodeHash",
                table: "MfaRecoveryCodes",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_UserId_UsedAt",
                table: "MfaRecoveryCodes",
                columns: new[] { "UserId", "UsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaChallenges");

            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropColumn(
                name: "LastTotpStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaEnabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                table: "Users");
        }
    }
}
