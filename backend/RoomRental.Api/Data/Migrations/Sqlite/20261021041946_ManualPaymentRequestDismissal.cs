using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ManualPaymentRequestDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DismissalReason",
                table: "ManualPaymentRequests",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DismissedAt",
                table: "ManualPaymentRequests",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DismissalReason",
                table: "ManualPaymentRequests");

            migrationBuilder.DropColumn(
                name: "DismissedAt",
                table: "ManualPaymentRequests");
        }
    }
}
