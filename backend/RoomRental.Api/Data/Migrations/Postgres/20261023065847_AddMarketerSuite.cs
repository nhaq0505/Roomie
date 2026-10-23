using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomRental.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddMarketerSuite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingPromoCode",
                table: "RegistrationVerifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                table: "MarketingCampaigns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarketingArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReadTimeLabel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    SeoTitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    SeoDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CtaLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CtaTargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    CtaClickCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingBanners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    CtaLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CtaUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PromoCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Placement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetSegment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Variant = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ImpressionCount = table.Column<int>(type: "integer", nullable: false),
                    ClickCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingBanners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingEmailCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetSegment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    BodyHtml = table.Column<string>(type: "text", nullable: false),
                    PromoCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CtaLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CtaUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TotalRecipients = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingEmailCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerMarketingNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMarketingNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerMarketingNotes_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OwnerMarketingNotes_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnerMarketingProfiles",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NextFollowUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastContactedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedMarketerId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMarketingProfiles", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_OwnerMarketingProfiles_Users_AssignedMarketerId",
                        column: x => x.AssignedMarketerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OwnerMarketingProfiles_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketingEmailRecipientLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingEmailRecipientLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingEmailRecipientLogs_MarketingEmailCampaigns_Campaig~",
                        column: x => x.CampaignId,
                        principalTable: "MarketingEmailCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketingEmailRecipientLogs_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingArticles_Slug",
                table: "MarketingArticles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingArticles_Status_PublishedAt",
                table: "MarketingArticles",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingBanners_Placement_IsActive",
                table: "MarketingBanners",
                columns: new[] { "Placement", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEmailCampaigns_Status_CreatedAt",
                table: "MarketingEmailCampaigns",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEmailRecipientLogs_CampaignId_OwnerId",
                table: "MarketingEmailRecipientLogs",
                columns: new[] { "CampaignId", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEmailRecipientLogs_OwnerId",
                table: "MarketingEmailRecipientLogs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMarketingNotes_AuthorUserId",
                table: "OwnerMarketingNotes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMarketingNotes_OwnerId_CreatedAt",
                table: "OwnerMarketingNotes",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMarketingProfiles_AssignedMarketerId",
                table: "OwnerMarketingProfiles",
                column: "AssignedMarketerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMarketingProfiles_LeadStatus",
                table: "OwnerMarketingProfiles",
                column: "LeadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMarketingProfiles_NextFollowUpAt",
                table: "OwnerMarketingProfiles",
                column: "NextFollowUpAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingArticles");

            migrationBuilder.DropTable(
                name: "MarketingBanners");

            migrationBuilder.DropTable(
                name: "MarketingEmailRecipientLogs");

            migrationBuilder.DropTable(
                name: "OwnerMarketingNotes");

            migrationBuilder.DropTable(
                name: "OwnerMarketingProfiles");

            migrationBuilder.DropTable(
                name: "MarketingEmailCampaigns");

            migrationBuilder.DropColumn(
                name: "PendingPromoCode",
                table: "RegistrationVerifications");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                table: "MarketingCampaigns");
        }
    }
}
