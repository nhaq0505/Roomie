using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using System.Text.Json;

namespace RoomRental.Api.Services;

public sealed record ManualPaymentAssessment(string Status, decimal Outstanding, bool CanConfirm, bool CanDismiss, string? Message)
{
    public static async Task<Dictionary<Guid, ManualPaymentAssessment>> LoadAsync(
        AppDbContext db, Guid ownerId, IReadOnlyList<ManualPaymentRequest> requests, CancellationToken ct)
    {
        var ids = requests.SelectMany(r => JsonSerializer.Deserialize<InvoiceAllocation[]>(r.AllocationsJson)!)
            .Select(x => x.InvoiceId).Distinct().ToArray();
        var invoices = await db.Invoices.Where(i => i.OwnerId == ownerId && ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var reserved = (await PaymentReservations.Pending(db).Where(i => i.OwnerId == ownerId && ids.Contains(i.Id))
            .Select(i => i.Id).ToListAsync(ct)).ToHashSet();
        return requests.ToDictionary(r => r.Id, r => Evaluate(r, invoices, reserved));
    }

    private static ManualPaymentAssessment Evaluate(ManualPaymentRequest request, IReadOnlyDictionary<Guid, Invoice> invoices, HashSet<Guid> reserved)
    {
        if (request.ConfirmedAt != null) return new("Confirmed", 0, false, false, null);
        if (request.DismissedAt != null) return new("Dismissed", 0, false, false, "Yêu cầu này đã được bỏ khỏi danh sách. Không ghi thêm khoản thu.");
        var allocations = JsonSerializer.Deserialize<InvoiceAllocation[]>(request.AllocationsJson)!;
        if (allocations.Length == 0 || allocations.Any(a => a.Amount <= 0) ||
            allocations.Select(a => a.InvoiceId).Distinct().Count() != allocations.Length || allocations.Sum(a => a.Amount) != request.Amount ||
            allocations.Any(a => !invoices.TryGetValue(a.InvoiceId, out var i) || i.OccupancyId != request.OccupancyId || i.LifecycleState != InvoiceLifecycleState.Issued))
            return new("Unavailable", 0, false, true, "Hóa đơn của yêu cầu này không còn khả dụng. Vui lòng đối chiếu trước khi bỏ khỏi danh sách.");
        var outstanding = allocations.Sum(a => Math.Max(0, invoices[a.InvoiceId].Total - invoices[a.InvoiceId].Paid));
        if (outstanding == 0)
            return new("AlreadyPaid", 0, false, true, "Các hóa đơn của yêu cầu này đã được thu đủ. Không ghi thêm khoản thu.");
        if (allocations.Any(a => invoices[a.InvoiceId].Total - invoices[a.InvoiceId].Paid != a.Amount))
            return new("InvoiceChanged", outstanding, false, true, "Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.");
        if (allocations.Any(a => reserved.Contains(a.InvoiceId)))
            return new("PaymentPending", outstanding, false, false, "Vui lòng đối soát giao dịch payOS đang chờ trước khi ghi thu.");
        // QR expiry governs reuse, not whether a bank transfer can be acknowledged.
        return new("Ready", outstanding, true, false, null);
    }
}
