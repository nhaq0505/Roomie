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

        // Admin-only monitoring endpoints
        var adminGroup = app.MapGroup("/api/admin/monitoring")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        adminGroup.MapGet("/growth", async (
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

        adminGroup.MapGet("/revenue", async (
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
            IOperationsMonitoringService opsService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await opsService.GetOperationsMetricsAsync(ct);
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

        adminGroup.MapGet("/campaigns", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            IRevenueMonitoringQueryService revenueService,
            CancellationToken ct) =>
        {
            var response = await revenueService.GetCampaignCostsAsync(fromDate, toDate, ct);
            return Results.Ok(response);
        });

        adminGroup.MapPost("/campaigns", async (
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
    }
}
