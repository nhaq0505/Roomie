using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class BillingEndpoints
{
    private static void MapSubscriptionRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/subscription", async (HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
            var currentRooms = await (from r in db.Rooms.AsNoTracking()
                                      join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                                      where b.OwnerId == ownerId && r.ArchivedAt == null && b.ArchivedAt == null
                                      select r.Id).CountAsync(cancellationToken);

            return Results.Ok(new SubscriptionStatusDto(
                owner.SubscriptionTier.ToString(),
                owner.IsPro,
                owner.SubscriptionExpiresAt,
                currentRooms,
                owner.MaxRooms,
                PayOsService.DefaultPackages
            ));
        });

        routes.MapPost("/subscription/upgrade", async (CreateSubscriptionCheckoutRequest request, HttpContext context, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var checkout = await payOsService.CreateSubscriptionCheckoutAsync(ownerId, request.PackageId, cancellationToken);
            return Results.Ok(checkout);
        }).RequireRateLimiting("tenant-payment");

        routes.MapPost("/subscription/reconcile/{orderCode:long}", async (long orderCode, HttpContext context, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var result = await payOsService.ReconcileSubscriptionOrderAsync(ownerId, orderCode, cancellationToken);
            return Results.Ok(result);
        }).RequireRateLimiting("tenant-payment");

    }
}
