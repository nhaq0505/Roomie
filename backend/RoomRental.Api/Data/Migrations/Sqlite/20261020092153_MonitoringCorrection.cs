using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MonitoringCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "RegisteredAt",
                table: "OwnerMilestones",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<double>(
                name: "MemoryPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<double>(
                name: "DiskPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<double>(
                name: "CpuPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AddColumn<string>(
                name: "EventId",
                table: "AnalyticsEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MarketingCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegistrationAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    VerificationMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BrowserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LandingPage = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReferrerPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationFacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventId",
                table: "AnalyticsEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingCampaigns_Channel_Source",
                table: "MarketingCampaigns",
                columns: new[] { "Channel", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingCampaigns_Code",
                table: "MarketingCampaigns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationFacts_OccurredAt",
                table: "RegistrationFacts",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationFacts_OwnerId",
                table: "RegistrationFacts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationFacts_RegistrationAttemptId_Stage",
                table: "RegistrationFacts",
                columns: new[] { "RegistrationAttemptId", "Stage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingCampaigns");

            migrationBuilder.DropTable(
                name: "RegistrationFacts");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_EventId",
                table: "AnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "AnalyticsEvents");

            migrationBuilder.AlterColumn<long>(
                name: "RegisteredAt",
                table: "OwnerMilestones",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "MemoryPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "DiskPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "CpuPercent",
                table: "HostMetrics",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);
        }
    }
}
