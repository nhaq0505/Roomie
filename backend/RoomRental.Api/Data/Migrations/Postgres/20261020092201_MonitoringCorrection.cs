using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class MonitoringCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RegisteredAt",
                table: "OwnerMilestones",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<double>(
                name: "MemoryPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "DiskPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "CpuPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<string>(
                name: "EventId",
                table: "AnalyticsEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MarketingCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UtmSource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VerificationMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BrowserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtmSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtmContent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtmTerm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LandingPage = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReferrerPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RegisteredAt",
                table: "OwnerMilestones",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "MemoryPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "DiskPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "CpuPercent",
                table: "HostMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);
        }
    }
}
