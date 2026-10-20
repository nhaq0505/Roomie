using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var ownerRoutes = app.MapGroup("/api").RequireAuthorization(policy => policy.RequireRole("Owner"));

        ownerRoutes.MapGet("/settings/payment", async (HttpContext context, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            return Results.Ok(await webhookService.GetAsync(ownerId, cancellationToken));
        });

        ownerRoutes.MapPut("/settings/payment", async (PaymentSettingsRequest request, HttpContext context, AppDbContext db, IDataProtectionProvider protectionProvider, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var clientId = request.ClientId?.Trim();
            var apiKey = request.ApiKey?.Trim();
            var checksumKey = request.ChecksumKey?.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập Client ID của payOS.");
            if (apiKey is { Length: > 1000 } || checksumKey is { Length: > 1000 })
                throw new ApiException(StatusCodes.Status400BadRequest, "Thông tin xác thực payOS quá dài.");

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            if (await db.PayOsAttempts.AnyAsync(row => row.OwnerId == ownerId && row.Status == PaymentAttemptStatus.Pending, cancellationToken))
                throw new ApiException(StatusCodes.Status409Conflict, "Vui lòng đối soát các giao dịch payOS đang chờ trước khi đổi thông tin thanh toán.");

            var settings = await db.PaymentSettings.SingleOrDefaultAsync(row => row.OwnerId == ownerId, cancellationToken);
            if (settings is null && (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(checksumKey)))
                throw new ApiException(StatusCodes.Status400BadRequest, "Cần nhập API key và checksum key khi thiết lập payOS lần đầu.");
            var protector = protectionProvider.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
            if (settings is null)
            {
                settings = new OwnerPaymentSettings
                {
                    OwnerId = ownerId,
                    ClientId = clientId,
                    ProtectedApiKey = protector.Protect(apiKey!),
                    ProtectedChecksumKey = protector.Protect(checksumKey!),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.PaymentSettings.Add(settings);
            }
            else
            {
                settings.ClientId = clientId;
                if (!string.IsNullOrWhiteSpace(apiKey)) settings.ProtectedApiKey = protector.Protect(apiKey);
                if (!string.IsNullOrWhiteSpace(checksumKey)) settings.ProtectedChecksumKey = protector.Protect(checksumKey);
                settings.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await webhookService.ConfirmAsync(ownerId, cancellationToken));
        });

        ownerRoutes.MapPost("/settings/payment/verify-webhook", async (HttpContext context, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
            Results.Ok(await webhookService.ConfirmAsync(ApiSupport.OwnerId(context), cancellationToken)))
            .RequireRateLimiting("tenant-payment");

        ownerRoutes.MapPost("/payments/reconcile/{orderCode:long}", async (long orderCode, HttpContext context, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            return Results.Ok(await payOsService.ReconcileAsync(ownerId, orderCode, cancellationToken));
        });

        app.MapGet("/api/tenant/{token}", async (string token, int? page, int? pageSize, Guid? invoiceId, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var roomBuilding = await (from room in db.Rooms.AsNoTracking()
                                      join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                                      where room.Id == occupancy.RoomId
                                      select new { Room = room, Building = building }).SingleAsync(cancellationToken);
            var roomDto = await OwnerManagementEndpoints.ToRoomDtoAsync(db, tokenService, webhookService, roomBuilding.Room, roomBuilding.Building,
                occupancy, VietnamCalendar.CurrentVietnamPeriod(), cancellationToken);
            var paging = PageRequest.Parse(page, pageSize);
            var invoiceQuery = db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().Where(invoice => invoice.OccupancyId == occupancy.Id && invoice.LifecycleState == InvoiceLifecycleState.Issued);
            if (invoiceId is not null) invoiceQuery = invoiceQuery.Where(x => x.Id == invoiceId);
            var total = await invoiceQuery.CountAsync(cancellationToken);
            var totals = new { outstanding = await QueryAggregates.SumMoneyAsync(db, invoiceQuery.Select(x => x.Total - x.Paid), cancellationToken), unpaidCount = await invoiceQuery.CountAsync(x => x.Total > x.Paid, cancellationToken) };
            var invoices = await invoiceQuery.OrderByDescending(x => x.Period).ThenByDescending(x => x.IssuedAt).ThenBy(x => x.Id)
                .Skip(paging.Offset).Take(paging.Size).ToListAsync(cancellationToken);
            var occupancyDto = roomDto.Occupancy ?? throw new ApiException(StatusCodes.Status404NotFound, "Tenant link is no longer active.");
            var paymentSettings = await webhookService.GetAsync(roomBuilding.Building.OwnerId, cancellationToken);
            return Results.Ok(new { confirmationMode = paymentSettings.ConfirmationMode, room = roomDto, occupancy = occupancyDto, invoices = invoices.Select(invoice => InvoiceService.ToDto(invoice)).ToList(), total, totals, page = paging.Number, pageSize = paging.Size });
        });

        app.MapPost("/api/tenant/{token}/payment", async (string token, AppDbContext db, PayOsService service, CancellationToken ct) => {
            var occupancy = await FindTenantAsync(db, token, ct);
            var invoice = await db.Invoices.Where(i => i.OccupancyId == occupancy.Id && i.LifecycleState == InvoiceLifecycleState.Issued && i.Total > i.Paid)
                .OrderBy(i => i.Id).FirstOrDefaultAsync(ct) ?? throw new ApiException(409, "Không còn hóa đơn cần thanh toán.");
            return Results.Ok(await service.CreateOrGetCheckoutAsync(invoice.OwnerId, invoice, token, ct, allInvoices: true));
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/orders/{orderCode:long}/reconcile", async (string token, long orderCode, AppDbContext db, PayOsService service, CancellationToken ct) => {
            var occupancy = await FindTenantAsync(db, token, ct);
            var attempt = await db.PayOsAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.OrderCode == orderCode && a.Kind == "Invoice" &&
                db.Invoices.Any(i => i.Id == a.InvoiceId && i.OccupancyId == occupancy.Id && i.OwnerId == a.OwnerId), ct)
                ?? throw new ApiException(404, "Payment order was not found for this tenant.");
            return Results.Ok(await service.ReconcileAsync(attempt.OwnerId, orderCode, ct));
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/invoices/{id:guid}/payment", async (string token, Guid id, AppDbContext db, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var invoice = await db.Invoices.SingleOrDefaultAsync(row => row.Id == id && row.OccupancyId == occupancy.Id && row.LifecycleState == InvoiceLifecycleState.Issued, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found for this tenant.");
            var checkout = await payOsService.CreateOrGetCheckoutAsync(invoice.OwnerId, invoice, token, cancellationToken);
            return Results.Ok(checkout);
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/invoices/{id:guid}/reconcile", async (string token, Guid id, AppDbContext db, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.OccupancyId == occupancy.Id && row.LifecycleState == InvoiceLifecycleState.Issued, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found for this tenant.");
            var status = await payOsService.ReconcileInvoiceAsync(invoice.OwnerId, invoice.Id, cancellationToken);
            var refreshed = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleAsync(row => row.Id == invoice.Id, cancellationToken);
            return Results.Ok(new { status, invoice = InvoiceService.ToDto(refreshed) });
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/payments/webhook/{ownerId:guid}", async (Guid ownerId, HttpRequest request, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            JsonDocument body;
            try { body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken); }
            catch (JsonException) { throw new ApiException(StatusCodes.Status400BadRequest, "Malformed payOS callback payload."); }
            using (body)
                await payOsService.ProcessWebhookAsync(ownerId, body.RootElement, cancellationToken);
            return Results.Ok(new { code = "00", desc = "success" });
        });
    }

    internal static async Task<Occupancy> FindTenantAsync(AppDbContext db, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            throw new ApiException(StatusCodes.Status404NotFound, "Tenant link was not found.");
        var tokenHash = TenantTokenService.Hash(token);
        return await db.Occupancies.SingleOrDefaultAsync(row => row.IsActive && row.TenantTokenHash == tokenHash, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Tenant link is no longer active.");
    }
}
