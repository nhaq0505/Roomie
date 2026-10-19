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
    private static void MapReceiptsRoutes(RouteGroupBuilder routes)
    {
        routes.MapPost("/invoices/{id:guid}/reconcile", async (Guid id, HttpContext context, AppDbContext db, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found.");
            var status = await payOsService.ReconcileInvoiceAsync(ownerId, invoice.Id, cancellationToken);
            var refreshed = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleAsync(row => row.Id == invoice.Id, cancellationToken);
            return Results.Ok(new { status, invoice = InvoiceService.ToDto(refreshed) });
        }).RequireRateLimiting("tenant-payment");

        routes.MapPost("/invoices/{id:guid}/receipts", async (Guid id, ReceiptRequest request, HttpContext context,
            ManualReceiptService receipts, CancellationToken cancellationToken) =>
            Results.Ok(await receipts.RecordAsync(ApiSupport.OwnerId(context), id, request,
                context.Request.Headers["Idempotency-Key"].FirstOrDefault(), cancellationToken)));

    }
}
