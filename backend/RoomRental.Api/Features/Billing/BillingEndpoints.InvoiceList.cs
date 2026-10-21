using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class BillingEndpoints
{
    private static void MapInvoiceListRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/invoices", async (string? period, string? kind, string? lifecycleState, string? status, string? search, Guid? occupancyId, int? page, int? pageSize, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var query = db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().Where(row => row.OwnerId == ownerId);
            if (!string.IsNullOrWhiteSpace(period))
            {
                var requestedPeriod = ApiSupport.NormalizePeriod(period);
                query = query.Where(row => row.Period == requestedPeriod);
            }
            if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(row => row.Kind == kind.Trim());
            if (!string.IsNullOrWhiteSpace(lifecycleState))
            {
                if (!Enum.TryParse<InvoiceLifecycleState>(lifecycleState, true, out var lifecycle) || !Enum.IsDefined(lifecycle))
                    throw new ApiException(400, "Trạng thái phát hành hóa đơn không hợp lệ.");
                query = query.Where(row => row.LifecycleState == lifecycle);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<InvoiceStatus>(status, true, out var invoiceStatus) || !Enum.IsDefined(invoiceStatus))
                    throw new ApiException(400, "Trạng thái thanh toán hóa đơn không hợp lệ.");
                query = query.Where(row => row.Status == invoiceStatus);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(row => row.RoomName.ToLower().Contains(term) ||
                    row.BuildingName.ToLower().Contains(term) || row.TenantName.ToLower().Contains(term));
            }
            if (occupancyId is not null) query = query.Where(x => x.OccupancyId == occupancyId);
            var paging = PageRequest.Parse(page, pageSize);
            var summary = await query.GroupBy(_ => 1).Select(group => new
            {
                total = group.Count(),
                billed = group.Sum(x => x.LifecycleState == InvoiceLifecycleState.Issued ? x.Total : 0m),
                paid = group.Sum(x => x.LifecycleState == InvoiceLifecycleState.Issued ? x.Paid : 0m),
                outstanding = group.Sum(x => x.LifecycleState == InvoiceLifecycleState.Issued ? x.Total - x.Paid : 0m),
                draftCount = group.Count(x => x.LifecycleState == InvoiceLifecycleState.Draft)
            }).SingleOrDefaultAsync(cancellationToken);
            var total = summary?.total ?? 0;
            var totals = new { billed = summary?.billed ?? 0m, paid = summary?.paid ?? 0m,
                outstanding = summary?.outstanding ?? 0m };
            var draftCount = summary?.draftCount ?? 0;
            var rows = await query.OrderByDescending(row => row.Period).ThenBy(row => row.BuildingName).ThenBy(row => row.RoomName).ThenBy(row => row.Id)
                .Skip(paging.Offset).Take(paging.Size).ToListAsync(cancellationToken);
            var rowIds = rows.Select(x => x.Id).ToList();
            var pendingIds = await PaymentReservations.Pending(db).Where(x => x.OwnerId == ownerId && rowIds.Contains(x.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken);
            var pending = pendingIds.ToHashSet();

            var occupancyIds = rows.Select(x => x.OccupancyId).Distinct().ToList();
            var occupancies = await db.Occupancies.AsNoTracking()
                .Where(x => occupancyIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            var canShareTenantLink = await tokenService.CanShareAsync(ownerId, webhookService, cancellationToken);
            var items = rows.Select(row =>
            {
                var token = canShareTenantLink && occupancies.TryGetValue(row.OccupancyId, out var occ) ? tokenService.Unprotect(occ) : null;
                return InvoiceService.ToDto(row, pending.Contains(row.Id), tenantToken: token);
            }).ToList();
            return Results.Ok(page is null ? (object)items : new { items, total, totals, draftCount, page = paging.Number, pageSize = paging.Size });
        });

        routes.MapPost("/rooms/{id:guid}/supplemental-invoices", async (Guid id, SupplementalInvoiceRequest request,
            HttpContext context, AppDbContext db, InvoiceService invoiceService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var description = request.Description?.Trim();
            if (request.RequestId == Guid.Empty || request.OccupancyId == Guid.Empty || string.IsNullOrWhiteSpace(description) || description.Length > 500 ||
                request.Amount <= 0 || request.Amount > int.MaxValue || request.Amount != decimal.Truncate(request.Amount) || request.DueDate == default)
                throw new ApiException(StatusCodes.Status400BadRequest, "Nhập nội dung, số tiền nguyên VND và hạn thanh toán hợp lệ.");
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            var existing = await db.Invoices.SingleOrDefaultAsync(x => x.Id == request.RequestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.OwnerId != ownerId || existing.RoomId != id || existing.OccupancyId != request.OccupancyId || existing.Kind != "Supplemental" ||
                    existing.Total != request.Amount || existing.Description != description || existing.DueDate != request.DueDate)
                    throw new ApiException(StatusCodes.Status409Conflict, "Mã yêu cầu đã được dùng cho hóa đơn khác.");
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(InvoiceService.ToDto(existing));
            }
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(x => x.Id == request.OccupancyId && x.RoomId == id && x.IsActive, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status409Conflict, "Người thuê đã thay đổi hoặc phòng chưa có người thuê. Vui lòng tải lại danh sách.");
            var invoice = new Invoice
            {
                Id = request.RequestId, OwnerId = ownerId, RoomId = id, BuildingId = building.Id, OccupancyId = occupancy.Id,
                Kind = "Supplemental", Description = description, RoomName = room.Name, BuildingName = building.Name,
                TenantName = occupancy.TenantName, Period = VietnamCalendar.CurrentVietnamPeriod(),
                Total = request.Amount, DueDate = request.DueDate
            };
            db.Invoices.Add(invoice);
            await invoiceService.StageZaloReminderAsync(invoice, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "InvoiceSupplemental", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Created($"/api/invoices/{invoice.Id}", InvoiceService.ToDto(invoice));
        });

        routes.MapPost("/invoices/generate", async (GenerateInvoiceRequest request, HttpContext context, InvoiceService invoiceService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var period = ApiSupport.NormalizePeriod(request.Period);
            var result = await invoiceService.GenerateForOwnerAsync(ownerId, period, cancellationToken, ActorType.Owner);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "InvoiceGenerate", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Ok(new { created = result.Created, skipped = result.Skipped, period });
        });

        routes.MapGet("/invoices/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found.");
            var pending = await db.PayOsAttempts.AsNoTracking().AnyAsync(x => x.OwnerId == ownerId && (x.InvoiceId == id || db.Invoices.Any(i => i.Id == id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, cancellationToken);
            var meterPolicy = await MeterCorrectionPolicyAsync(db, invoice, cancellationToken);
            var occupancy = await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invoice.OccupancyId, cancellationToken);
            var token = occupancy is not null && await tokenService.CanShareAsync(invoice.OwnerId, webhookService, cancellationToken)
                ? tokenService.Unprotect(occupancy) : null;
            var overlap = invoice.Kind == "Supplemental" && invoice.IsFinalMeterReading && await db.Invoices.AnyAsync(x => x.OwnerId == ownerId && x.OccupancyId == invoice.OccupancyId && x.Period == invoice.Period && x.Kind == "Monthly", cancellationToken);
            return Results.Ok(InvoiceService.ToDto(invoice, pending, meterPolicy.Allowed, meterPolicy.Reason, tenantToken: token) with { HasMonthlyOverlap = overlap });
        });

        routes.MapPost("/invoices/{id:guid}/publish", async (Guid id, PublishInvoiceRequest request, HttpContext context, AppDbContext db, InvoiceService invoiceService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken ct = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (request.RequestId == Guid.Empty || request.ExpectedVersion < 1)
                throw new ApiException(400, "Mã yêu cầu và phiên bản hóa đơn hợp lệ là bắt buộc.");
            var amounts = new[] { request.Rent, request.ElectricityAmount, request.WaterAmount, request.ServiceAmount };
            var total = InvoiceService.ValidateTotal(amounts);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Invoice was not found.");
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Owner published the partial-month draft." : request.Reason.Trim();
            if (reason.Length > 1000) throw new ApiException(400, "Reason must be 1,000 characters or fewer.");
            var fingerprint = RequestFingerprint.Fingerprint(new { request.ExpectedVersion, request.Rent, request.ElectricityAmount, request.WaterAmount, request.ServiceAmount, reason });
            var priorRevision = await db.InvoiceRevisions.SingleOrDefaultAsync(x => x.InvoiceId == id && x.RequestId == request.RequestId, ct);
            if (priorRevision is not null)
            {
                if (priorRevision.RequestFingerprint != fingerprint) throw new ApiException(409, "This request ID was already used for different invoice changes.");
                await transaction.CommitAsync(ct);
                return Results.Ok(InvoiceService.ToDto(invoice));
            }
            if (invoice.LifecycleState != InvoiceLifecycleState.Draft || invoice.Kind != "Monthly")
                throw new ApiException(409, "Only a monthly draft can be published.");
            if (invoice.Version != request.ExpectedVersion) throw new ApiException(409, "Invoice changed. Refresh it and retry.");
            if (invoice.Paid != 0 || await db.Receipts.AnyAsync(x => x.InvoiceId == id, ct))
                throw new ApiException(409, "A draft with a recorded payment cannot be edited or published.");
            if (await db.PayOsAttempts.AnyAsync(x => (x.InvoiceId == id || db.Invoices.Any(i => i.Id == id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                throw new ApiException(409, "Reconcile the pending payOS attempt before publishing this invoice.");

            var before = SerializeSnapshot(invoice);
            invoice.Rent = request.Rent;
            invoice.ElectricityAmount = request.ElectricityAmount;
            invoice.WaterAmount = request.WaterAmount;
            invoice.ServiceAmount = request.ServiceAmount;
            invoice.Total = total;
            var now = DateTimeOffset.UtcNow;
            invoice.LifecycleState = InvoiceLifecycleState.Issued;
            invoice.IssuedAt = now;
            invoice.Version++;
            InvoiceService.RecalculateStatus(invoice);
            var after = SerializeSnapshot(invoice);
            await AddRevisionAsync(db, invoice, request.RequestId, fingerprint, reason, ApiSupport.UserId(context), before, after, ct);
            await invoiceService.StageZaloReminderAsync(invoice, ct);
            var outbox = new MilestoneOutboxEvent
            {
                OwnerId = ownerId,
                EventType = "InvoiceIssued",
                TargetId = $"{invoice.Id}:Owner",
                OccurredAt = now,
                Status = "Pending"
            };
            db.MilestoneOutboxEvents.Add(outbox);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (milestoneTracker != null)
            {
                try
                {
                    await milestoneTracker.TrackInvoiceIssuedAsync(ownerId, invoice.Id, invoice.Kind, ActorType.Owner, now, ct);
                    var milestoneRecorded = await db.OwnerMilestones.AnyAsync(m => m.OwnerId == ownerId && m.ActivatedAt != null, ct);
                    if (milestoneRecorded)
                    {
                        outbox.Status = "Completed";
                        outbox.ProcessedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch { /* Telemetry failure must not block product; outbox remains Pending */ }
            }
            return Results.Ok(InvoiceService.ToDto(invoice));
        });

    }
}
