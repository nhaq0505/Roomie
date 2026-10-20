using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MilestoneOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MilestoneOutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilestoneOutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneOutboxEvents_OwnerId_EventType",
                table: "MilestoneOutboxEvents",
                columns: new[] { "OwnerId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneOutboxEvents_Status_OccurredAt",
                table: "MilestoneOutboxEvents",
                columns: new[] { "Status", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MilestoneOutboxEvents");
        }
    }
}
