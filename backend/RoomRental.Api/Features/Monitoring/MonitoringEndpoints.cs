using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RoomRental.Api.Domain;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        // Public client analytics intake endpoint
        app.MapPost("/api/analytics/events", async (
            HttpContext context,
            AnalyticsEventBatchRequest request,
            IClientAnalyticsService analyticsService,
            CancellationToken ct) =>
        {
            if (!ApiSupport.IsSameOrigin(context.Request))
            {
                throw new ApiException(StatusCodes.Status403Forbidden, "Nguồn gửi yêu cầu không hợp lệ.");
            }

            Guid? authenticatedOwnerId = null;
            if (context.User.Identity?.IsAuthenticated == true &&
                context.User.IsInRole("Owner") &&
                Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                authenticatedOwnerId = uid;
            }

            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            var response = await analyticsService.RecordClientEventsAsync(request, authenticatedOwnerId, clientIp, ct);
            return Results.Ok(response);
        }).RequireRateLimiting("analytics");

        // Growth, Revenue & Campaign endpoints (Admin + Marketer)
        var marketingGroup = app.MapGroup("/api/admin/monitoring")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Marketer"));

        // Operations, Incidents & Maintenance endpoints (Admin-only)
        var adminGroup = app.MapGroup("/api/admin/monitoring")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        marketingGroup.MapGet("/growth", async (
            [AsParameters] MonitoringFilterRequest filter,
            IGrowthMonitoringQueryService growthService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await growthService.GetGrowthMetricsAsync(filter, ct);
                return Results.Ok(response);
            }
            catch (NotImplementedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
        });

        marketingGroup.MapGet("/revenue", async (
            [AsParameters] MonitoringFilterRequest filter,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await revenueService.GetRevenueMetricsAsync(filter, ct);
                return Results.Ok(response);
            }
            catch (NotImplementedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
        });

        adminGroup.MapGet("/operations", async (
            [FromQuery] string? window,
            IOperationsMonitoringService opsService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await opsService.GetOperationsMetricsAsync(window ?? "1h", ct);
                return Results.Ok(response);
            }
            catch (NotImplementedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
        });

        adminGroup.MapGet("/incidents", async (
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await incidentService.GetIncidentsAsync(ct);
                return Results.Ok(response);
            }
            catch (NotImplementedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
        });

        adminGroup.MapPatch("/incidents/{id:guid}", async (
            Guid id,
            UpdateIncidentRequest request,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await incidentService.UpdateIncidentStateAsync(id, request, ct);
                return Results.Ok(response);
            }
            catch (NotImplementedException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
        });

        // Campaign Metadata CRUD
        marketingGroup.MapGet("/campaigns/metadata", async (
            bool? activeOnly,
            string? channel,
            string? source,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var response = await revenueService.GetCampaignsAsync(activeOnly, channel, source, ct);
            return Results.Ok(response);
        });

        marketingGroup.MapPost("/campaigns/metadata", async (
            CreateCampaignRequest request,
            HttpContext context,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var adminId = ApiSupport.UserId(context);
            var adminName = context.User.Identity?.Name ?? "Admin";
            var response = await revenueService.CreateCampaignAsync(request, adminId, adminName, ct);
            return Results.Created($"/api/admin/monitoring/campaigns/metadata/{response.Id}", response);
        });

        marketingGroup.MapPut("/campaigns/metadata/{id:guid}", async (
            Guid id,
            UpdateCampaignRequest request,
            HttpContext context,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var adminId = ApiSupport.UserId(context);
            var adminName = context.User.Identity?.Name ?? "Admin";
            var response = await revenueService.UpdateCampaignAsync(id, request, adminId, adminName, ct);
            return Results.Ok(response);
        });

        // Campaign Costs CRUD
        marketingGroup.MapGet("/campaigns", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? campaign,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var response = await revenueService.GetCampaignCostsAsync(fromDate, toDate, campaign, ct);
            return Results.Ok(response);
        });

        marketingGroup.MapGet("/campaigns/costs", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? campaign,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var response = await revenueService.GetCampaignCostsAsync(fromDate, toDate, campaign, ct);
            return Results.Ok(response);
        });

        marketingGroup.MapPost("/campaigns", async (
            UpsertCampaignCostRequest request,
            HttpContext context,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var adminId = ApiSupport.UserId(context);
            var adminName = context.User.Identity?.Name ?? "Admin";
            var response = await revenueService.UpsertCampaignCostAsync(request, adminId, adminName, ct);
            return Results.Ok(response);
        });

        marketingGroup.MapPost("/campaigns/costs", async (
            UpsertCampaignCostRequest request,
            HttpContext context,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var adminId = ApiSupport.UserId(context);
            var adminName = context.User.Identity?.Name ?? "Admin";
            var response = await revenueService.UpsertCampaignCostAsync(request, adminId, adminName, ct);
            return Results.Ok(response);
        });

        marketingGroup.MapGet("/campaigns/generate-url", (
            [FromQuery] string targetUrl,
            [FromQuery] string? source,
            [FromQuery] string? medium,
            [FromQuery] string? campaign,
            [FromQuery] string? term,
            [FromQuery] string? content,
            [FromQuery] string? promo,
            IRevenueMonitoringQueryService revenueService) =>
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return Results.BadRequest(new { message = "targetUrl is required" });
            var trackingUrl = revenueService.BuildTrackingUrl(targetUrl, source, medium, campaign, term, content, promo);
            return Results.Ok(new { url = trackingUrl });
        });

        // Maintenance Endpoints
        adminGroup.MapPost("/maintenance/rollup", async (
            [FromQuery] DateOnly? date,
            IMonitoringMaintenanceService maintenanceService,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var targetDate = date ?? ApiSupport.VietnamDate(clock.GetUtcNow());
            var result = await maintenanceService.RollupDailyAggregateAsync(targetDate, ct);
            return Results.Ok(result);
        });

        adminGroup.MapPost("/maintenance/prune", async (
            IMonitoringMaintenanceService maintenanceService,
            CancellationToken ct) =>
        {
            var count = await maintenanceService.PruneExpiredTelemetryAsync(ct);
            return Results.Ok(new { prunedCount = count });
        });
    }
}
