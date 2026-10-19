using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class DepositEndpoints
{
    public static void MapDepositEndpoints(this WebApplication app)
    {
        app.MapPost("/api/invoices/{id:guid}/deposit-settlements", async (Guid id, DepositSettlementRequest request,
            HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (request.Kind is not ("Refund" or "Deduction") || request.Amount <= 0 ||
                request.Amount > int.MaxValue || request.Amount != decimal.Truncate(request.Amount) || string.IsNullOrWhiteSpace(request.Reason) ||
                request.Reason.Trim().Length > 500 || !Guid.TryParse(request.IdempotencyKey, out _))
                throw new ApiException(400, "Nhập loại quyết toán, số tiền nguyên VND, lý do và mã thao tác hợp lệ.");
            if (request.Kind == "Refund" && request.TargetInvoiceId is not null)
                throw new ApiException(400, "Hoàn cọc không thể liên kết hóa đơn công nợ.");
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements)
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.Kind == "Deposit", cancellationToken)
                ?? throw new ApiException(404, "Không tìm thấy khoản cọc.");
            var previous = invoice.DepositSettlements.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (previous is not null)
            {
                if (previous.Amount != request.Amount || previous.Kind != request.Kind || previous.Reason != request.Reason.Trim() || previous.TargetInvoiceId != request.TargetInvoiceId)
                    throw new ApiException(409, "Mã thao tác đã được dùng cho nội dung khác.");
                return Results.Ok(InvoiceService.ToDto(invoice));
            }
            if (await db.MoveoutCompletions.AnyAsync(x => x.OwnerId == ownerId && x.OccupancyId == invoice.OccupancyId, cancellationToken))
                throw new ApiException(409, "Lượt thuê đã được quyết toán trả phòng. Không thể thay đổi khoản cọc sau khi kết thúc.");
            if (invoice.LifecycleState != InvoiceLifecycleState.Issued)
                throw new ApiException(409, "Draft deposit invoices cannot be settled.");
            if (request.ExpectedVersion is long expectedVersion && invoice.Version != expectedVersion)
                throw new ApiException(409, "Deposit invoice changed. Refresh it and retry the settlement.");
            if (await db.PayOsAttempts.AnyAsync(x => x.InvoiceId == id && x.Status == PaymentAttemptStatus.Pending, cancellationToken))
                throw new ApiException(409, "Đối soát giao dịch cọc đang chờ trước khi quyết toán.");
            if (request.Amount > invoice.Paid - invoice.DepositSettlements.Sum(x => x.Amount))
                throw new ApiException(400, "Số tiền vượt quá cọc đã nhận và đang giữ.");
            if (request.TargetInvoiceId is Guid targetId)
            {
                var target = await db.Invoices.SingleOrDefaultAsync(x => x.Id == targetId && x.OwnerId == ownerId &&
                    x.OccupancyId == invoice.OccupancyId && (x.Kind == "Monthly" || x.Kind == "Supplemental") &&
                    x.LifecycleState == InvoiceLifecycleState.Issued, cancellationToken)
                    ?? throw new ApiException(404, "Hóa đơn công nợ phải thuộc cùng người thuê.");
                if (request.TargetExpectedVersion is long expectedTargetVersion && target.Version != expectedTargetVersion)
                    throw new ApiException(409, "Debt invoice changed. Refresh it and retry the settlement.");
                if (request.Amount > target.Total - target.Paid)
                    throw new ApiException(400, "Khấu trừ vượt số tiền còn nợ của hóa đơn.");
                if (await db.PayOsAttempts.AnyAsync(x => x.InvoiceId == targetId && x.Status == PaymentAttemptStatus.Pending, cancellationToken))
                    throw new ApiException(409, "Đối soát giao dịch hóa đơn đang chờ trước khi khấu trừ.");
                db.Receipts.Add(new PaymentReceipt { OwnerId = ownerId, InvoiceId = target.Id, Amount = request.Amount,
                    Source = "Deposit", Note = request.Reason.Trim(), IdempotencyKey = request.IdempotencyKey });
                target.Paid += request.Amount;
                target.Version++;
                InvoiceService.RecalculateStatus(target);
            }
            var settlement = new DepositSettlement { InvoiceId = id, Amount = request.Amount,
                Kind = request.Kind, Reason = request.Reason.Trim(), IdempotencyKey = request.IdempotencyKey,
                TargetInvoiceId = request.TargetInvoiceId, RecordedAt = ApiSupport.UtcIsoString(DateTimeOffset.UtcNow) };
            db.DepositSettlements.Add(settlement);
            invoice.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(InvoiceService.ToDto(invoice));
        }).RequireAuthorization(policy => policy.RequireRole("Owner"));
    }
}
