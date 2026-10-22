using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapOccupanciesRoutes(RouteGroupBuilder routes)
    {
        routes.MapMethods("/rooms/{id:guid}/occupancy", ["PATCH"], async (Guid id, OccupancyUpdateRequest request,
            HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(x => x.Id == request.OccupancyId && x.RoomId == id && x.IsActive, ct)
                ?? throw new ApiException(409, "Lượt thuê đã thay đổi hoặc không còn hoạt động. Vui lòng tải lại phòng.");
            if (string.IsNullOrWhiteSpace(request.TenantName) || request.TenantName.Trim().Length > 160)
                throw new ApiException(400, "Tên người thuê cần từ 1 đến 160 ký tự.");
            if (request.Phone?.Trim().Length > 60) throw new ApiException(400, "Số điện thoại tối đa 60 ký tự.");
            if (request.People is < 1 or > 30) throw new ApiException(400, "Số người ở cần từ 1 đến 30.");
            var peopleChanged = occupancy.People != request.People;
            occupancy.TenantName = request.TenantName.Trim();
            occupancy.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            occupancy.People = request.People;
            if (peopleChanged)
            {
                // Issued invoices (including unpaid ones) are finalized snapshots. Only unpaid drafts may change.
                var drafts = await db.Invoices.Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                    x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Draft && x.WaterPerPerson == true).ToListAsync(ct);
                foreach (var draft in drafts)
                {
                    if (draft.Paid != 0 || draft.Status == InvoiceStatus.Paid ||
                        await db.Receipts.AnyAsync(x => x.InvoiceId == draft.Id, ct) ||
                        await db.PayOsAttempts.AnyAsync(x => (x.InvoiceId == draft.Id || db.Invoices.Any(i => i.Id == draft.Id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                        throw new ApiException(409, "Hóa đơn nháp có thanh toán hoặc giao dịch đang chờ. Cần xử lý trước khi đổi số người.");
                    if (draft.WaterRate is null) throw new ApiException(409, "Hóa đơn nháp thiếu bảng phí nước đã lưu.");
                    var before = BillingEndpoints.SerializeSnapshot(draft);
                    draft.WaterAmount = ApiSupport.RoundVnd(draft.WaterRate.Value * occupancy.People);
                    draft.Total = InvoiceService.ValidateTotal(draft.Rent, draft.ElectricityAmount, draft.WaterAmount, draft.ServiceAmount);
                    draft.Version++;
                    InvoiceService.RecalculateStatus(draft);
                    await BillingEndpoints.AddRevisionAsync(db, draft, Guid.NewGuid(), RequestFingerprint.Fingerprint(request),
                        "Cập nhật số người ở cho hóa đơn nháp.", ownerId, before, BillingEndpoints.SerializeSnapshot(draft), ct);
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(await ToRoomDtoAsync(db, tokenService, webhookService, room, building, occupancy, VietnamCalendar.CurrentVietnamPeriod(), ct));
        });

        routes.MapPost("/rooms/{id:guid}/occupancy", async (Guid id, OccupancyRequest request, HttpContext context, AppDbContext db, InvoiceService invoiceService, TenantTokenService tokenService, PaymentWebhookService webhookService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            if (await db.Occupancies.AnyAsync(row => row.RoomId == id && row.IsActive, cancellationToken))
                throw new ApiException(StatusCodes.Status409Conflict, "This room already has an active occupancy.");
            if (string.IsNullOrWhiteSpace(request.TenantName) || request.TenantName.Trim().Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tenant name is required and must be 160 characters or fewer.");
            if (request.People is < 1 or > 30)
                throw new ApiException(StatusCodes.Status400BadRequest, "People must be between 1 and 30.");
            var today = VietnamCalendar.TodayVietnam();
            if (request.StartDate > today)
                throw new ApiException(StatusCodes.Status400BadRequest, "Start date cannot be in the future.");
            var currentPeriod = VietnamCalendar.CurrentVietnamPeriod();

            if (request.DepositAmount < 0 || request.DepositAmount > int.MaxValue || request.DepositAmount != decimal.Truncate(request.DepositAmount))
                throw new ApiException(400, "Tiền cọc phải là số nguyên VND từ 0 đến 2.147.483.647.");

            if (request.InitialReading is decimal initialReading)
            {
                if (initialReading < 0 || initialReading > 1_000_000_000)
                    throw new ApiException(400, "Chỉ số điện phải là số không âm trong giới hạn cho phép.");
                if (decimal.Round(initialReading, 3) != initialReading)
                    throw new ApiException(400, "Chỉ số điện tối đa 3 chữ số thập phân.");
                var maxReading = await QueryAggregates.MaxReadingAsync(db,
                    db.Readings.AsNoTracking().Where(x => x.RoomId == id).Select(x => x.CurrentReading), cancellationToken);
                var maxFinal = await QueryAggregates.MaxReadingAsync(db,
                    db.Invoices.AsNoTracking().Where(x => x.RoomId == id && x.IsFinalMeterReading).Select(x => x.CurrentReading), cancellationToken);
                var priorPhysical = Math.Max(room.InitialReading, Math.Max(maxReading, maxFinal));
                if (priorPhysical > 0 && initialReading < priorPhysical)
                    throw new ApiException(400, $"Chỉ số điện không thể thấp hơn chỉ số trước đó ({priorPhysical} kWh).");
                room.InitialReading = initialReading;
            }

            var token = tokenService.Create();
            var occupancy = new Occupancy
            {
                RoomId = id,
                TenantName = request.TenantName.Trim(),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim()[..Math.Min(request.Phone.Trim().Length, 60)],
                People = request.People,
                StartDate = request.StartDate,
                TenantTokenHash = token.Hash,
                ProtectedTenantToken = token.ProtectedToken
            };
            db.Occupancies.Add(occupancy);
            if (request.DepositAmount > 0)
            {
                var deposit = new Invoice { OwnerId = ownerId, RoomId = room.Id, OccupancyId = occupancy.Id,
                    BuildingId = building.Id, RoomName = room.Name, BuildingName = building.Name, TenantName = occupancy.TenantName,
                    Period = currentPeriod, Kind = "Deposit", Total = request.DepositAmount, DueDate = today };
                db.Invoices.Add(deposit);
                await invoiceService.StageZaloReminderAsync(deposit, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "OccupancyMoveIn", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Ok(await ToRoomDtoAsync(db, tokenService, webhookService, room, building, occupancy, currentPeriod, cancellationToken));
        });

        routes.MapPost("/rooms/{id:guid}/end-occupancy", async (Guid id, CompleteMoveoutRequest request,
            HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
            await MoveoutEndpoints.CompleteMoveoutAsync(id, request, context, db, milestoneTracker, cancellationToken));

        routes.MapPost("/rooms/{id:guid}/rotate-link", async (Guid id, HttpContext context, AppDbContext db,
            TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (!await tokenService.CanShareAsync(ownerId, webhookService, cancellationToken))
                throw new ApiException(StatusCodes.Status400BadRequest, "Chưa hoàn tất cấu hình nhận tiền (VietQR thủ công hoặc payOS tự động). Vui lòng thiết lập thanh toán trước khi cấp link cho khách.");

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            _ = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(row => row.RoomId == id && row.IsActive, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status409Conflict, "This room has no active occupancy.");
            var token = tokenService.Create();
            occupancy.TenantTokenHash = token.Hash;
            occupancy.ProtectedTenantToken = token.ProtectedToken;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new RotateLinkDto(token.Token, tokenService.Url(token.Token)));
        });
    }
}
