using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ZaloReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZaloReminders",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    QueuedAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttemptAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    SentAtMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloReminders", x => x.InvoiceId);
                    table.ForeignKey(
                        name: "FK_ZaloReminders_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ZaloSettings",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduleEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    Hour = table.Column<int>(type: "INTEGER", nullable: false),
                    Minute = table.Column<int>(type: "INTEGER", nullable: false),
                    BankBin = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    BankAccount = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BankAccountName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastScheduledPeriod = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    DispatchLeaseUntilMs = table.Column<long>(type: "INTEGER", nullable: false),
                    NextSendAtMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloSettings", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_ZaloSettings_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZaloReminders_OwnerId_Status_QueuedAtMs",
                table: "ZaloReminders",
                columns: new[] { "OwnerId", "Status", "QueuedAtMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZaloReminders");

            migrationBuilder.DropTable(
                name: "ZaloSettings");
        }
    }
}
