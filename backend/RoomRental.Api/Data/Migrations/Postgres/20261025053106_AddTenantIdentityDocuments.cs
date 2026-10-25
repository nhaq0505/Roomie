using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddTenantIdentityDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantIdentityDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccupancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PhotoFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    PhotoContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PhotoSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PhotoSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhotoContent = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIdentityDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantIdentityDocuments_OccupancyId_TargetKey",
                table: "TenantIdentityDocuments",
                columns: new[] { "OccupancyId", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantIdentityDocuments_OwnerId_OccupancyId",
                table: "TenantIdentityDocuments",
                columns: new[] { "OwnerId", "OccupancyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantIdentityDocuments");
        }
    }
}
