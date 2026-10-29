using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWaterMeterBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InitialWaterReading",
                table: "Rooms",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "WaterByMeter",
                table: "Rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentWaterReading",
                table: "Readings",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousWaterReading",
                table: "Readings",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WaterByMeter",
                table: "Readings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WaterConsumption",
                table: "Readings",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentWaterReading",
                table: "Invoices",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousWaterReading",
                table: "Invoices",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WaterByMeter",
                table: "Invoices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WaterConsumption",
                table: "Invoices",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialWaterReading",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "WaterByMeter",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "CurrentWaterReading",
                table: "Readings");

            migrationBuilder.DropColumn(
                name: "PreviousWaterReading",
                table: "Readings");

            migrationBuilder.DropColumn(
                name: "WaterByMeter",
                table: "Readings");

            migrationBuilder.DropColumn(
                name: "WaterConsumption",
                table: "Readings");

            migrationBuilder.DropColumn(
                name: "CurrentWaterReading",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PreviousWaterReading",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "WaterByMeter",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "WaterConsumption",
                table: "Invoices");
        }
    }
}
