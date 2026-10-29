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
    private static void MapCorrectionsRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/invoices/{id:guid}/corrections", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Invoice was not found.");
            var revisions = await (from revision in db.InvoiceRevisions.AsNoTracking()
                                   join actor in db.Users.AsNoTracking() on revision.ActorId equals actor.Id
                                   where revision.InvoiceId == id
                                   orderby revision.Revision
                                   select new InvoiceCorrectionDto(revision.Id, revision.Revision, revision.RequestId, revision.Reason,
                                       revision.ActorId, actor.Name, revision.CreatedAt, revision.Version, revision.BeforeJson, revision.AfterJson))
                .ToListAsync(ct);
            var pending = await db.PayOsAttempts.AsNoTracking().AnyAsync(x => x.OwnerId == ownerId && (x.InvoiceId == id || db.Invoices.Any(i => i.Id == id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct);
            var meterPolicy = await MeterCorrectionPolicyAsync(db, invoice, ct);
            return Results.Ok(new { invoiceId = id, version = invoice.Version, hasPendingPayment = pending,
                canCorrectMeter = meterPolicy.Allowed, meterCorrectionBlockedReason = meterPolicy.Reason, items = revisions });
        });

        routes.MapPost("/invoices/{id:guid}/corrections", async (Guid id, InvoiceCorrectionRequest request, HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken ct = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var actorId = ApiSupport.UserId(context);
            var reason = request.Reason?.Trim();
            if (request.RequestId == Guid.Empty || request.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
                throw new ApiException(400, "Mã yêu cầu, phiên bản và lý do hợp lệ là bắt buộc.");
            if (request.Description is { Length: > 500 }) throw new ApiException(400, "Description must be 500 characters or fewer.");
            if (request.CurrentReading is < 0 or > 1_000_000_000 || (request.CurrentReading is decimal meter && decimal.Round(meter, 3) != meter))
                throw new ApiException(400, "Chỉ số mới phải nằm trong giới hạn và có tối đa ba chữ số thập phân.");
            if (request.CurrentWaterReading is < 0 or > 1_000_000_000 || (request.CurrentWaterReading is decimal waterMeter && decimal.Round(waterMeter, 3) != waterMeter))
                throw new ApiException(400, "Chỉ số nước mới phải nằm trong giới hạn và có tối đa ba chữ số thập phân.");

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Invoice was not found.");
            var fingerprint = request.CurrentWaterReading is null
                ? RequestFingerprint.Fingerprint(new { request.ExpectedVersion, Reason = reason, request.Rent, request.ElectricityAmount,
                request.WaterAmount, request.ServiceAmount, request.CurrentReading, request.DueDate, request.Description, request.Amount })
                : RequestFingerprint.Fingerprint(new { request.ExpectedVersion, Reason = reason, request.Rent, request.ElectricityAmount,
                request.WaterAmount, request.ServiceAmount, request.CurrentReading, request.CurrentWaterReading, request.DueDate, request.Description, request.Amount });
            var prior = await db.InvoiceRevisions.SingleOrDefaultAsync(x => x.InvoiceId == id && x.RequestId == request.RequestId, ct);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint) throw new ApiException(409, "This request ID was already used for different invoice changes.");
                await transaction.CommitAsync(ct);
                return Results.Ok(new { invoice = InvoiceService.ToDto(invoice), correction = ToCorrectionDto(prior) });
            }
            if (invoice.Version != request.ExpectedVersion) throw new ApiException(409, "Invoice changed. Refresh it and retry.");
            if (invoice.LifecycleState != InvoiceLifecycleState.Issued || invoice.Paid != 0 || await db.Receipts.AnyAsync(x => x.InvoiceId == id, ct))
                throw new ApiException(409, "Only an issued invoice with no recorded receipts can be corrected.");
            if (await db.PayOsAttempts.AnyAsync(x => x.OwnerId == ownerId && (x.InvoiceId == id || db.Invoices.Any(i => i.Id == id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                throw new ApiException(409, "Reconcile the pending payOS attempt before correcting this invoice.");
            if (invoice.Kind == "Deposit" && invoice.DepositSettlements.Count != 0)
                throw new ApiException(409, "A deposit invoice with settlement records cannot be corrected.");

            var before = SerializeSnapshot(invoice);
            var changed = false;
            if (invoice.Kind == "Monthly")
            {
                if (request.Amount is not null) throw new ApiException(400, "Use monthly charge fields to correct a monthly invoice.");
                var meterChanged = request.CurrentReading is not null;
                var waterMeterChanged = request.CurrentWaterReading is not null;
                MonthlyReading? reading = null;
                if (meterChanged)
                {
                    var policy = await MeterCorrectionPolicyAsync(db, invoice, ct);
                    if (!policy.Allowed) throw new ApiException(409, policy.Reason ?? "Meter correction is no longer allowed.");
                    reading = await db.Readings.SingleOrDefaultAsync(x => x.OccupancyId == invoice.OccupancyId && x.Period == invoice.Period, ct)
                        ?? throw new ApiException(409, "The source meter reading no longer exists.");
                    if (request.CurrentReading < reading.PreviousReading)
                        throw new ApiException(400, "Chỉ số mới không thể thấp hơn chỉ số trước.");
                    reading.CurrentReading = request.CurrentReading is decimal currentReading
                        ? currentReading
                        : throw new ApiException(400, "Chỉ số mới là bắt buộc.");
                    reading.Consumption = reading.CurrentReading - reading.PreviousReading;
                    invoice.CurrentReading = reading.CurrentReading;
                    invoice.Consumption = reading.Consumption;
                    changed = true;
                }
                if (waterMeterChanged)
                {
                    var policy = await MeterCorrectionPolicyAsync(db, invoice, ct);
                    if (!policy.Allowed) throw new ApiException(409, policy.Reason ?? "Meter correction is no longer allowed.");
                    reading ??= await db.Readings.SingleOrDefaultAsync(x => x.OccupancyId == invoice.OccupancyId && x.Period == invoice.Period, ct)
                        ?? throw new ApiException(409, "The source meter reading no longer exists.");
                    var prevWater = reading.PreviousWaterReading ?? invoice.PreviousWaterReading ?? 0;
                    if (request.CurrentWaterReading < prevWater)
                        throw new ApiException(400, "Chỉ số nước mới không thể thấp hơn chỉ số nước trước.");
                    reading.PreviousWaterReading = prevWater;
                    reading.CurrentWaterReading = request.CurrentWaterReading!.Value;
                    reading.WaterConsumption = reading.CurrentWaterReading.Value - prevWater;
                    invoice.PreviousWaterReading = prevWater;
                    invoice.CurrentWaterReading = reading.CurrentWaterReading;
                    invoice.WaterConsumption = reading.WaterConsumption;
                    changed = true;
                }
                if (request.Rent is decimal rent) { invoice.Rent = ValidateCharge(rent); changed = true; }
                if (request.ElectricityAmount is decimal electricity) { invoice.ElectricityAmount = ValidateCharge(electricity); changed = true; }
                else if (meterChanged && invoice.ElectricityRate is decimal rate)
                {
                    invoice.ElectricityAmount = ApiSupport.RoundVnd(invoice.Consumption * rate);
                    changed = true;
                }
                if (request.WaterAmount is decimal water) { invoice.WaterAmount = ValidateCharge(water); changed = true; }
                else if (request.CurrentWaterReading is not null && invoice.WaterByMeter == true && invoice.WaterRate is decimal waterRate && invoice.WaterConsumption is decimal waterCons)
                {
                    invoice.WaterAmount = ApiSupport.RoundVnd(waterCons * waterRate);
                    changed = true;
                }
                if (request.ServiceAmount is decimal service) { invoice.ServiceAmount = ValidateCharge(service); changed = true; }
                if ((request.CurrentReading is not null || request.CurrentWaterReading is not null) && reading is not null) changed = true;
                invoice.Total = InvoiceService.ValidateTotal(invoice.Rent, invoice.ElectricityAmount, invoice.WaterAmount, invoice.ServiceAmount);
            }
            else if (invoice.Kind == "Deposit")
            {
                if (request.Rent is not null || request.ElectricityAmount is not null || request.WaterAmount is not null || request.ServiceAmount is not null || request.CurrentReading is not null)
                    throw new ApiException(400, "Use the amount field to correct a deposit invoice.");
                if (request.Amount is decimal amount) { invoice.Total = ValidateCharge(amount); changed = true; }
            }
            else if (invoice.IsFinalMeterReading)
            {
                var overlappingMonthly = await db.Invoices.AsNoTracking().AnyAsync(x => x.OccupancyId == invoice.OccupancyId &&
                    x.Period == invoice.Period && x.Kind == "Monthly" && x.Id != invoice.Id, ct);
                if (overlappingMonthly && (request.Rent is > 0 || request.WaterAmount is > 0))
                    throw new ApiException(409, "Không thể thêm tiền thuê hoặc tiền nước vì kỳ này đã có hóa đơn tháng.");
                if (request.CurrentReading is decimal currentReading)
                {
                    var policy = await MeterCorrectionPolicyAsync(db, invoice, ct);
                    if (!policy.Allowed) throw new ApiException(409, policy.Reason ?? "Meter correction is no longer allowed.");
                    if (currentReading < invoice.PreviousReading) throw new ApiException(400, "Chỉ số mới không thể thấp hơn chỉ số trước.");
                    invoice.CurrentReading = currentReading;
                    invoice.Consumption = currentReading - invoice.PreviousReading;
                    var occupancy = await db.Occupancies.SingleAsync(x => x.Id == invoice.OccupancyId, ct);
                    if (!occupancy.IsActive)
                    {
                        // Completion copied this final anchor into the room baseline. Keep
                        // that baseline synchronized when the latest final reading changes.
                        var room = await db.Rooms.SingleAsync(x => x.Id == invoice.RoomId, ct);
                        room.InitialReading = currentReading;
                    }
                    if (request.ElectricityAmount is null && invoice.ElectricityRate is decimal rate)
                        invoice.ElectricityAmount = ApiSupport.RoundVnd(invoice.Consumption * rate);
                    changed = true;
                }
                if (request.Rent is decimal rent) { invoice.Rent = ValidateCharge(rent); changed = true; }
                if (request.ElectricityAmount is decimal electricity) { invoice.ElectricityAmount = ValidateCharge(electricity); changed = true; }
                if (request.WaterAmount is decimal water) { invoice.WaterAmount = ValidateCharge(water); changed = true; }
                else if (request.CurrentWaterReading is not null && invoice.WaterByMeter == true && invoice.WaterRate is decimal waterRate && invoice.WaterConsumption is decimal waterCons)
                {
                    invoice.WaterAmount = ApiSupport.RoundVnd(waterCons * waterRate);
                    changed = true;
                }
                if (request.ServiceAmount is decimal service) { invoice.ServiceAmount = ValidateCharge(service); changed = true; }
                if (request.Amount is not null) throw new ApiException(400, "Use meter or charge fields to correct a final meter invoice.");
                invoice.Total = InvoiceService.ValidateTotal(invoice.Rent, invoice.ElectricityAmount, invoice.WaterAmount, invoice.ServiceAmount);
            }
            else
            {
                if (request.Rent is not null || request.ElectricityAmount is not null || request.WaterAmount is not null || request.ServiceAmount is not null || request.CurrentReading is not null)
                    throw new ApiException(400, "Use the amount field to correct this invoice.");
                if (request.Amount is decimal amount) { invoice.Total = ValidateCharge(amount); changed = true; }
            }
            if (request.DueDate is DateOnly dueDate) { invoice.DueDate = dueDate; changed = true; }
            if (request.Description is string description) { invoice.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(); changed = true; }
            if (!changed) throw new ApiException(400, "Supply at least one invoice field to correct.");
            invoice.Version++;
            InvoiceService.RecalculateStatus(invoice);
            var after = SerializeSnapshot(invoice);
            var revision = await AddRevisionAsync(db, invoice, request.RequestId, fingerprint, reason, actorId, before, after, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "InvoiceCorrection", ct); }
                catch { /* Telemetry failure must not block product */ }
            }
            var policyAfter = await MeterCorrectionPolicyAsync(db, invoice, ct);
            var pendingAfter = await db.PayOsAttempts.AsNoTracking().AnyAsync(x => (x.InvoiceId == id || db.Invoices.Any(i => i.Id == id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct);
            return Results.Ok(new { invoice = InvoiceService.ToDto(invoice, pendingAfter, policyAfter.Allowed, policyAfter.Reason), correction = ToCorrectionDto(revision) });
        });

    }
}
