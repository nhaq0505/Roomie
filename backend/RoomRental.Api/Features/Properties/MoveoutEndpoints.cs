using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class MoveoutEndpoints
{
    internal static async Task<decimal> MeterBaselineAsync(AppDbContext db, Room room, CancellationToken ct)
    {
        var reading = await QueryAggregates.MaxReadingAsync(db,
            db.Readings.AsNoTracking().Where(x => x.RoomId == room.Id).Select(x => x.CurrentReading), ct);
        var legacyFinal = await QueryAggregates.MaxReadingAsync(db,
            db.Invoices.AsNoTracking().Where(x => x.RoomId == room.Id && x.IsFinalMeterReading).Select(x => x.CurrentReading), ct);
        return Math.Max(room.InitialReading, Math.Max(reading, legacyFinal));
    }

    public static void MapMoveoutEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/rooms").RequireAuthorization(policy => policy.RequireRole("Owner"));

        routes.MapGet("/{id:guid}/moveout", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var (room, _) = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);
            var occupancy = await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == id && x.IsActive, ct)
                ?? throw new ApiException(409, "Phòng chưa có người thuê đang ở.");
            var invoices = await db.Invoices.AsNoTracking().Include(x => x.DepositSettlements)
                .Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id)
                .OrderBy(x => x.IssuedAt).ToListAsync(ct);
            var invoiceIds = invoices.Select(x => x.Id).ToArray();
            var pendingInvoiceIds = await PaymentReservations.Pending(db).Where(x => x.OwnerId == ownerId && invoiceIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            var deposit = invoices.SingleOrDefault(x => x.Kind == "Deposit");
            var held = deposit is null ? 0 : deposit.Paid - deposit.DepositSettlements.Sum(x => x.Amount);
            var blockedReason = CompletionBlockReason(invoices, pendingInvoiceIds);
            var previousCompletion = await db.MoveoutCompletions.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.RoomId == id)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                occupancyId = occupancy.Id,
                tenantName = occupancy.TenantName,
                roomName = room.Name,
                startDate = occupancy.StartDate,
                endDate = VietnamCalendar.TodayVietnam(),
                invoices = invoices.Select(x => InvoiceService.ToDto(x, pendingInvoiceIds.Contains(x.Id))),
                pendingInvoiceIds,
                deposit = deposit is null ? null : new
                {
                    invoiceId = deposit.Id,
                    required = deposit.Total,
                    paid = deposit.Paid,
                    settled = deposit.DepositSettlements.Sum(x => x.Amount),
                    held,
                    outstanding = Math.Max(0, deposit.Total - deposit.Paid),
                    version = deposit.Version
                },
                heldDeposit = held,
                canComplete = blockedReason is null,
                blockedReason,
                previousCompletion = previousCompletion is null ? null : ToCompletionDto(previousCompletion)
            });
        });

        routes.MapGet("/{id:guid}/moveout-completions", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            _ = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct, allowArchived: true);
            var items = await db.MoveoutCompletions.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.RoomId == id)
                .OrderByDescending(x => x.CompletedAt).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(ToCompletionDto) });
        });

        routes.MapPost("/{id:guid}/complete-moveout", CompleteMoveoutAsync);
    }

    internal static async Task<IResult> CompleteMoveoutAsync(Guid id, CompleteMoveoutRequest request,
        HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken ct = default)
    {
        var ownerId = ApiSupport.OwnerId(context);
        if (request.OccupancyId == Guid.Empty || request.RequestId == Guid.Empty)
            throw new ApiException(400, "Mã lượt thuê và mã thao tác hợp lệ là bắt buộc.");
        ValidateAmount(request.RefundAmount, "Số tiền hoàn");
        ValidateAmount(request.ExpectedHeld, "Số tiền cọc đang giữ");
        var fingerprint = RequestFingerprint.Fingerprint(new
        {
            ownerId,
            roomId = id,
            request.OccupancyId,
            request.RefundAmount,
            request.ExpectedHeld,
            request.ExpectedDepositVersion
        });

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        _ = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);

        var previous = await db.MoveoutCompletions.SingleOrDefaultAsync(x => x.RequestId == request.RequestId, ct);
        if (previous is not null)
        {
            if (previous.OwnerId != ownerId || previous.RoomId != id || previous.OccupancyId != request.OccupancyId ||
                previous.RequestFingerprint != fingerprint)
                throw new ApiException(409, "Mã thao tác đã được dùng cho nội dung trả phòng khác.");
            await tx.CommitAsync(ct);
            return Results.Ok(ToCompletionDto(previous));
        }

        var occupancy = await db.Occupancies.SingleOrDefaultAsync(x => x.Id == request.OccupancyId && x.RoomId == id, ct)
            ?? throw new ApiException(404, "Không tìm thấy lượt thuê.");
        var priorForOccupancy = await db.MoveoutCompletions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OccupancyId == occupancy.Id, ct);
        if (priorForOccupancy is not null || !occupancy.IsActive)
            throw new ApiException(409, "Lượt thuê này đã kết thúc. Tải lại dữ liệu trước khi tiếp tục.");

        var invoices = await db.Invoices.Include(x => x.DepositSettlements)
            .Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id).ToListAsync(ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var pendingInvoiceIds = await PaymentReservations.Pending(db).Where(x => x.OwnerId == ownerId && invoiceIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
        var blockedReason = CompletionBlockReason(invoices, pendingInvoiceIds);
        if (blockedReason is not null) throw new ApiException(409, blockedReason);

        var deposit = invoices.SingleOrDefault(x => x.Kind == "Deposit");
        var held = deposit is null ? 0 : deposit.Paid - deposit.DepositSettlements.Sum(x => x.Amount);
        if (held < 0) throw new ApiException(409, "Số tiền quyết toán cọc hiện tại không hợp lệ. Hãy đối chiếu khoản cọc trước.");
        if (request.ExpectedHeld != held || request.ExpectedDepositVersion != deposit?.Version)
            throw new ApiException(409, "Khoản cọc đã thay đổi từ lần xem trước. Tải lại số tiền đang giữ rồi xác nhận lại.");
        if (request.RefundAmount > held)
            throw new ApiException(400, "Số tiền hoàn không thể vượt quá khoản cọc đang giữ.");

        var retained = held - request.RefundAmount;
        var endDate = VietnamCalendar.TodayVietnam();
        var completedAt = DateTimeOffset.UtcNow;
        var actorId = ApiSupport.UserId(context);
        var completion = new MoveoutCompletion
        {
            RequestId = request.RequestId,
            OwnerId = ownerId,
            RoomId = id,
            OccupancyId = occupancy.Id,
            ActorId = actorId,
            RequestFingerprint = fingerprint,
            HeldBeforeSettlement = held,
            RefundAmount = request.RefundAmount,
            RetainedAmount = retained,
            EndDate = endDate,
            CompletedAt = completedAt
        };

        if (deposit is not null)
        {
            if (request.RefundAmount > 0)
                db.DepositSettlements.Add(new DepositSettlement
                {
                    InvoiceId = deposit.Id,
                    Amount = request.RefundAmount,
                    Kind = "Refund",
                    Reason = $"Manual move-out refund ({request.RequestId:D}).",
                    IdempotencyKey = SettlementKey(request.RequestId, "refund"),
                    RecordedAt = ApiSupport.UtcIsoString(completedAt)
                });
            if (retained > 0)
                db.DepositSettlements.Add(new DepositSettlement
                {
                    InvoiceId = deposit.Id,
                    Amount = retained,
                    Kind = "Deduction",
                    Reason = $"Deposit retained by owner at move-out by manual agreement ({request.RequestId:D}).",
                    IdempotencyKey = SettlementKey(request.RequestId, "retain"),
                    RecordedAt = ApiSupport.UtcIsoString(completedAt)
                });
            if (request.RefundAmount > 0 || retained > 0) deposit.Version++;
        }

        var room = await db.Rooms.SingleAsync(x => x.Id == id, ct);
        room.InitialReading = Math.Max(room.InitialReading, await MeterBaselineAsync(db, room, ct));
        occupancy.IsActive = false;
        occupancy.EndDate = endDate;
        occupancy.TenantTokenHash = null;
        occupancy.ProtectedTenantToken = null;
        db.MoveoutCompletions.Add(completion);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (milestoneTracker != null)
        {
            try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "OccupancyMoveOut", ct); }
            catch { /* Telemetry failure must not block product */ }
        }
        return Results.Ok(ToCompletionDto(completion));
    }

    private static string? CompletionBlockReason(IReadOnlyCollection<Invoice> invoices, IReadOnlyCollection<Guid> pendingInvoiceIds)
    {
        if (invoices.Any(x => x.LifecycleState == InvoiceLifecycleState.Draft))
            return "Cần xử lý hóa đơn nháp trước khi kết thúc lượt thuê.";
        if (pendingInvoiceIds.Count > 0)
            return "Còn giao dịch payOS đang chờ. Đối soát trước khi trả phòng.";
        if (invoices.Any(x => x.LifecycleState == InvoiceLifecycleState.Issued && x.Kind != "Deposit" && x.Paid < x.Total))
            return "Cần thu đủ hoặc khấu trừ hết công nợ trước khi trả phòng.";
        var deposit = invoices.SingleOrDefault(x => x.Kind == "Deposit");
        if (deposit is not null && deposit.Paid < deposit.Total)
            return "Cần thu đủ khoản cọc đã thỏa thuận trước khi kết thúc lượt thuê.";
        return null;
    }

    private static string SettlementKey(Guid requestId, string kind) => $"moveout:{requestId:N}:{kind}";

    private static void ValidateAmount(decimal amount, string name)
    {
        if (amount < 0 || amount > int.MaxValue || amount != decimal.Truncate(amount))
            throw new ApiException(400, $"{name} phải là số VND nguyên từ 0 đến giới hạn thanh toán.");
    }

    private static object ToCompletionDto(MoveoutCompletion completion) => new
    {
        success = true,
        id = completion.Id,
        requestId = completion.RequestId,
        ownerId = completion.OwnerId,
        roomId = completion.RoomId,
        occupancyId = completion.OccupancyId,
        actorId = completion.ActorId,
        heldBeforeSettlement = completion.HeldBeforeSettlement,
        refundAmount = completion.RefundAmount,
        refundedAmount = completion.RefundAmount,
        retainedAmount = completion.RetainedAmount,
        endDate = completion.EndDate,
        completedAt = completion.CompletedAt
    };
}
