using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class PasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordRecoveries",
                columns: table => new
                {
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PasswordBinding = table.Column<string>(type: "TEXT", nullable: false),
                    ChallengeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedCode = table.Column<string>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSentAt = table.Column<long>(type: "INTEGER", nullable: false),
                    WindowStartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SendCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ResetHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResetExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ConsumedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NotificationPending = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordRecoveries", x => x.Email);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveries_ChallengeHash",
                table: "PasswordRecoveries",
                column: "ChallengeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveries_ExpiresAt",
                table: "PasswordRecoveries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveries_ResetHash",
                table: "PasswordRecoveries",
                column: "ResetHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordRecoveries");
        }
    }
}
