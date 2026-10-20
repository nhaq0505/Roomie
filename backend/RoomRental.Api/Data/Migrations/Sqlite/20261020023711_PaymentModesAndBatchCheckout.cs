using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class PaymentModesAndBatchCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualAccountName",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualAccountNumber",
                table: "Users",
                type: "TEXT",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualBankBin",
                table: "Users",
                type: "TEXT",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentConfirmationMode",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Automatic");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceAllocationsJson",
                table: "PayOsAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentReservationId",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManualPaymentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccupancyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllocationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BankBin = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ConfirmedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualPaymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualPaymentRequests_Occupancies_OccupancyId",
                        column: x => x.OccupancyId,
                        principalTable: "Occupancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManualPaymentRequests_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_OccupancyId",
                table: "ManualPaymentRequests",
                column: "OccupancyId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_OwnerId_CreatedAt",
                table: "ManualPaymentRequests",
                columns: new[] { "OwnerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualPaymentRequests");

            migrationBuilder.DropColumn(
                name: "ManualAccountName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ManualAccountNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ManualBankBin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PaymentConfirmationMode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvoiceAllocationsJson",
                table: "PayOsAttempts");

            migrationBuilder.DropColumn(
                name: "PaymentReservationId",
                table: "Invoices");
        }
    }
}
