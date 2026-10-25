using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WhatsAppReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppReminders",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    QueuedAtMs = table.Column<long>(type: "bigint", nullable: false),
                    LastAttemptAtMs = table.Column<long>(type: "bigint", nullable: false),
                    SentAtMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppReminders", x => x.InvoiceId);
                    table.ForeignKey(
                        name: "FK_WhatsAppReminders_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppSettings",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    Hour = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    BankBin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    BankAccount = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BankAccountName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastScheduledPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    DispatchLeaseUntilMs = table.Column<long>(type: "bigint", nullable: false),
                    NextSendAtMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppSettings", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_WhatsAppSettings_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppReminders_OwnerId_Status_QueuedAtMs",
                table: "WhatsAppReminders",
                columns: new[] { "OwnerId", "Status", "QueuedAtMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppReminders");

            migrationBuilder.DropTable(
                name: "WhatsAppSettings");
        }
    }
}
