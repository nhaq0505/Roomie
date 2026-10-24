using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddTenantSelfCheckinAndMeterSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TenantSelfCheckinAt",
                table: "Occupancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantMeterSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccupancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    PreviousReading = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SubmittedReading = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ApprovedReading = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    TenantNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RejectReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PhotoFileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PhotoContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhotoSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PhotoSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhotoContent = table.Column<byte[]>(type: "bytea", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadingId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMeterSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantMeterSubmissions_OccupancyId_Period",
                table: "TenantMeterSubmissions",
                columns: new[] { "OccupancyId", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMeterSubmissions_OwnerId_Period",
                table: "TenantMeterSubmissions",
                columns: new[] { "OwnerId", "Period" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantMeterSubmissions");

            migrationBuilder.DropColumn(
                name: "TenantSelfCheckinAt",
                table: "Occupancies");
        }
    }
}
