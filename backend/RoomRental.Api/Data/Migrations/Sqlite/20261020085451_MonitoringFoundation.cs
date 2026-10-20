using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MonitoringFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BrowserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EventName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ReferrerPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CtaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClientTimestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerTimestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Campaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CashVnd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PersonHours = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyAggregateMetrics",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalSessions = table.Column<int>(type: "INTEGER", nullable: false),
                    DistinctVisitors = table.Column<int>(type: "INTEGER", nullable: false),
                    Registrations = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivatedOwners = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveOwnersWau = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveOwnersMau = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscriptionRevenueVnd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MarketingSpendVnd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MarketingHours = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    TotalRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    Total5xxErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    CalculatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAggregateMetrics", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "HostMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryPercent = table.Column<double>(type: "REAL", nullable: false),
                    DiskPercent = table.Column<double>(type: "REAL", nullable: false),
                    ContainerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContainerStatusJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    HealthEndpointStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HostStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CollectedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncidentKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ResolvedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastNotifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NotificationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MetricName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Count = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerDailyActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OwnerActionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AdminActionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SystemActionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HasOwnerAction = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastActionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FirstActionAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastActionAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerDailyActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerDailyActivities_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnerMilestones",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstActivatedInvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FirstPaidAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstPaidOrderCode = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMilestones", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_OwnerMilestones_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationAttributions",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BrowserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchMedium = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchContent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchTerm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FirstTouchLandingPage = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FirstTouchAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastTouchSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastTouchMedium = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastTouchContent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastTouchTerm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastTouchLandingPage = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastTouchAt = table.Column<long>(type: "INTEGER", nullable: true),
                    VerificationMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AutoApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationAttributions", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_RegistrationAttributions_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteMinuteBuckets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MinuteTimestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    RouteTemplate = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe50ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe100ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe250ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe500ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe1000ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyLe2000ms = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyGt2000ms = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalLatencyMs = table.Column<double>(type: "REAL", nullable: false),
                    MaxLatencyMs = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteMinuteBuckets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkerHeartbeats",
                columns: table => new
                {
                    WorkerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastStartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastCompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastSuccessAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastErrorAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ConsecFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerHeartbeats", x => x.WorkerName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_BrowserId_ServerTimestamp",
                table: "AnalyticsEvents",
                columns: new[] { "BrowserId", "ServerTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType_ServerTimestamp",
                table: "AnalyticsEvents",
                columns: new[] { "EventType", "ServerTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OwnerId_ServerTimestamp",
                table: "AnalyticsEvents",
                columns: new[] { "OwnerId", "ServerTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_ServerTimestamp",
                table: "AnalyticsEvents",
                column: "ServerTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_SessionId_ServerTimestamp",
                table: "AnalyticsEvents",
                columns: new[] { "SessionId", "ServerTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_Date",
                table: "CampaignCosts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_Date_Campaign",
                table: "CampaignCosts",
                columns: new[] { "Date", "Campaign" });

            migrationBuilder.CreateIndex(
                name: "IX_HostMetrics_RecordedAt",
                table: "HostMetrics",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringIncidents_IncidentKey_State",
                table: "MonitoringIncidents",
                columns: new[] { "IncidentKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringIncidents_StartedAt",
                table: "MonitoringIncidents",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringIncidents_State_UpdatedAt",
                table: "MonitoringIncidents",
                columns: new[] { "State", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalCounters_PeriodStart_Category_MetricName",
                table: "OperationalCounters",
                columns: new[] { "PeriodStart", "Category", "MetricName" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDailyActivities_ActivityDate_HasOwnerAction",
                table: "OwnerDailyActivities",
                columns: new[] { "ActivityDate", "HasOwnerAction" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDailyActivities_OwnerId_ActivityDate",
                table: "OwnerDailyActivities",
                columns: new[] { "OwnerId", "ActivityDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMilestones_ActivatedAt",
                table: "OwnerMilestones",
                column: "ActivatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMilestones_FirstPaidAt",
                table: "OwnerMilestones",
                column: "FirstPaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMinuteBuckets_MinuteTimestamp",
                table: "RouteMinuteBuckets",
                column: "MinuteTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMinuteBuckets_MinuteTimestamp_RouteTemplate_StatusCode",
                table: "RouteMinuteBuckets",
                columns: new[] { "MinuteTimestamp", "RouteTemplate", "StatusCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "CampaignCosts");

            migrationBuilder.DropTable(
                name: "DailyAggregateMetrics");

            migrationBuilder.DropTable(
                name: "HostMetrics");

            migrationBuilder.DropTable(
                name: "MonitoringIncidents");

            migrationBuilder.DropTable(
                name: "OperationalCounters");

            migrationBuilder.DropTable(
                name: "OwnerDailyActivities");

            migrationBuilder.DropTable(
                name: "OwnerMilestones");

            migrationBuilder.DropTable(
                name: "RegistrationAttributions");

            migrationBuilder.DropTable(
                name: "RouteMinuteBuckets");

            migrationBuilder.DropTable(
                name: "WorkerHeartbeats");
        }
    }
}
