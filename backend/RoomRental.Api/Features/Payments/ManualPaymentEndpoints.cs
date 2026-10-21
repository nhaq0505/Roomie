using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class ManualPaymentEndpoints
{
    private static object Dto(ManualPaymentRequest request, string? roomName = null, string? tenantName = null, ManualPaymentAssessment? assessment = null) {
        var description = $"RM{request.Id:N}"[..22].ToUpperInvariant();
        return new { roomName, tenantName, request.Id, request.OccupancyId, request.Amount, request.BankBin, request.AccountNumber, request.AccountName,
            request.CreatedAt, request.ExpiresAt, request.ConfirmedAt, request.DismissedAt, description,
            status = assessment?.Status ?? (request.ConfirmedAt != null ? "Confirmed" : request.DismissedAt != null ? "Dismissed" : "Ready"),
            canConfirm = assessment?.CanConfirm ?? (request.ConfirmedAt == null && request.DismissedAt == null),
            canDismiss = assessment?.CanDismiss ?? false, currentOutstanding = assessment?.Outstanding ?? (request.ConfirmedAt != null ? 0 : request.Amount), statusMessage = assessment?.Message,
            invoices = JsonSerializer.Deserialize<InvoiceAllocation[]>(request.AllocationsJson)!,
            qrUrl = $"https://img.vietqr.io/image/{request.BankBin}-{Uri.EscapeDataString(request.AccountNumber)}-compact2.png?amount={request.Amount.ToString("0", CultureInfo.InvariantCulture)}&addInfo={Uri.EscapeDataString(description)}&accountName={Uri.EscapeDataString(request.AccountName)}" };
    }
    public static void MapManualPaymentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/tenant/{token}/manual-payments", async (string token, CreateManualPaymentRequest body, AppDbContext db, CancellationToken ct) => {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var occupancy = await PaymentEndpoints.FindTenantAsync(db, token, ct);
            var owner = await db.Users.AsNoTracking().SingleAsync(x => db.Buildings.Any(b => b.OwnerId == x.Id && db.Rooms.Any(r => r.Id == occupancy.RoomId && r.BuildingId == b.Id)), ct);
            if (owner.Status != OwnerStatus.Approved || owner.PaymentConfirmationMode != PaymentConfirmationMode.Manual || owner.ManualBankBin is null)
                throw new ApiException(409, "Chủ trọ chưa bật thanh toán VietQR thủ công.");
            var invoices = await db.Invoices.Where(i => i.OwnerId == owner.Id && i.OccupancyId == occupancy.Id &&
                i.LifecycleState == InvoiceLifecycleState.Issued && i.Total > i.Paid && (body.InvoiceId == null || i.Id == body.InvoiceId))
                .OrderBy(i => i.Id).ToListAsync(ct);
            if (invoices.Count == 0) throw new ApiException(409, "Không còn hóa đơn cần thanh toán.");
            var ids = invoices.Select(i => i.Id).ToArray();
            if (await db.PayOsAttempts.AnyAsync(a => a.Status == PaymentAttemptStatus.Pending &&
                db.Invoices.Any(i => ids.Contains(i.Id) && (i.Id == a.InvoiceId || i.PaymentReservationId == a.Id)), ct))
                throw new ApiException(409, "Vui lòng đối soát giao dịch payOS đang chờ trước khi thanh toán thủ công.");
            var allocations = invoices.Select(i => new InvoiceAllocation(i.Id, i.Total - i.Paid)).ToArray();
            var total = allocations.Sum(i => i.Amount);
            if (total > int.MaxValue || allocations.Any(x => x.Amount != decimal.Truncate(x.Amount))) throw new ApiException(400, "Tổng tiền vượt giới hạn thanh toán VND.");
            // Reopening the same selection reuses a live snapshot instead of creating duplicate requests.
            var json = JsonSerializer.Serialize(allocations);
            var now = DateTimeOffset.UtcNow;
            var candidates = await db.ManualPaymentRequests.Where(x => x.OwnerId == owner.Id && x.OccupancyId == occupancy.Id &&
                x.ConfirmedAt == null && x.DismissedAt == null && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
            var request = candidates.FirstOrDefault(x => x.AllocationsJson == json && x.BankBin == owner.ManualBankBin &&
                x.AccountNumber == owner.ManualAccountNumber && x.AccountName == owner.ManualAccountName);
            if (request is null) {
                request = new ManualPaymentRequest { OwnerId = owner.Id, OccupancyId = occupancy.Id, Amount = total, AllocationsJson = json,
                    BankBin = owner.ManualBankBin, AccountNumber = owner.ManualAccountNumber!, AccountName = owner.ManualAccountName! };
                db.ManualPaymentRequests.Add(request); await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct); return Results.Ok(Dto(request));
        }).RequireRateLimiting("tenant-payment");
        app.MapGet("/api/tenant/{token}/manual-payments/{id:guid}", async (string token, Guid id, AppDbContext db, CancellationToken ct) => {
            var occupancy = await PaymentEndpoints.FindTenantAsync(db, token, ct);
            var request = await db.ManualPaymentRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OccupancyId == occupancy.Id, ct)
                ?? throw new ApiException(404, "Không tìm thấy yêu cầu thanh toán.");
            var assessment = (await ManualPaymentAssessment.LoadAsync(db, request.OwnerId, [request], ct))[request.Id];
            return Results.Ok(Dto(request, assessment: assessment));
        });
        var routes = app.MapGroup("/api/manual-payments").RequireAuthorization(p => p.RequireRole("Owner"));
        routes.MapGet("", async (int? page, int? pageSize, HttpContext context, AppDbContext db, CancellationToken ct) => {
            var ownerId = ApiSupport.OwnerId(context);
            var paging = PageRequest.Parse(page, pageSize);
            var source = db.ManualPaymentRequests.AsNoTracking().Where(x => x.OwnerId == ownerId && x.ConfirmedAt == null && x.DismissedAt == null);
            var total = await source.CountAsync(ct);
            var requests = await source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(paging.Offset).Take(paging.Size).ToListAsync(ct);
            var occupancyIds = requests.Select(x => x.OccupancyId).ToArray();
            var labels = await (from occupancy in db.Occupancies.AsNoTracking()
                                join room in db.Rooms.AsNoTracking() on occupancy.RoomId equals room.Id
                                where occupancyIds.Contains(occupancy.Id)
                                select new { occupancy.Id, RoomName = room.Name, occupancy.TenantName }).ToDictionaryAsync(x => x.Id, ct);
            var assessments = await ManualPaymentAssessment.LoadAsync(db, ownerId, requests, ct);
            return Results.Ok(new { items = requests.Select(x => Dto(x, labels.GetValueOrDefault(x.OccupancyId)?.RoomName,
                labels.GetValueOrDefault(x.OccupancyId)?.TenantName, assessments[x.Id])), total, page = paging.Number, pageSize = paging.Size });
        });
        routes.MapPost("/{id:guid}/confirm", async (Guid id, ConfirmManualPaymentRequest body, HttpContext context, AppDbContext db, CancellationToken ct) => {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var ownerId = ApiSupport.OwnerId(context);
            var request = await db.ManualPaymentRequests.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Không tìm thấy yêu cầu thanh toán.");
            if (body.Amount != request.Amount) throw new ApiException(409, "Số tiền nhận phải khớp tổng tiền của yêu cầu thanh toán.");
            if (request.ConfirmedAt is not null) return Results.Ok(Dto(request));
            var assessment = (await ManualPaymentAssessment.LoadAsync(db, ownerId, [request], ct))[request.Id];
            if (!assessment.CanConfirm) throw new ApiException(409, assessment.Message!);
            var allocations = JsonSerializer.Deserialize<InvoiceAllocation[]>(request.AllocationsJson)!;
            foreach (var item in allocations.OrderBy(x => x.InvoiceId)) {
                // The assessment loaded and validated every member before any financial write.
                var invoice = db.Invoices.Local.Single(i => i.Id == item.InvoiceId);
                db.Receipts.Add(new PaymentReceipt { OwnerId = ownerId, InvoiceId = invoice.Id, Amount = item.Amount, Source = "Manual",
                    IdempotencyKey = $"manual:{request.Id:N}", ExternalReference = $"manual:{request.Id:N}:{invoice.Id:N}",
                    Note = $"Chuyển khoản VietQR {request.Id:N}", CreatedAt = DateTimeOffset.UtcNow });
                invoice.Paid += item.Amount; invoice.Version++; InvoiceService.RecalculateStatus(invoice);
            }
            request.ConfirmedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Results.Ok(Dto(request));
        });
        routes.MapPost("/{id:guid}/dismiss", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) => {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var ownerId = ApiSupport.OwnerId(context);
            var request = await db.ManualPaymentRequests.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Không tìm thấy yêu cầu thanh toán.");
            if (request.ConfirmedAt != null || request.DismissedAt != null) return Results.Ok(Dto(request));
            var assessment = (await ManualPaymentAssessment.LoadAsync(db, ownerId, [request], ct))[request.Id];
            if (!assessment.CanDismiss) throw new ApiException(409, "Yêu cầu vẫn còn hiệu lực. Vui lòng kiểm tra hóa đơn trước khi bỏ khỏi danh sách.");
            request.DismissedAt = DateTimeOffset.UtcNow;
            request.DismissalReason = assessment.Status;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(Dto(request));
        });
    }
}
