using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class BillingEndpoints
{
    public static void MapReadingsRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/readings", async (string? period, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var requestedPeriod = period is null ? VietnamCalendar.CurrentVietnamPeriod() : ApiSupport.NormalizePeriod(period);
            var rows = await (from reading in db.Readings.AsNoTracking()
                              join room in db.Rooms.AsNoTracking() on reading.RoomId equals room.Id
                              join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                              where building.OwnerId == ownerId && reading.Period == requestedPeriod
                              orderby building.Name, room.Name
                              select new { Reading = reading, Room = room, Building = building })
                .ToListAsync(cancellationToken);
            var occupancyIds = rows.Select(row => row.Reading.OccupancyId).Distinct().ToList();
            var invoices = await db.Invoices.AsNoTracking().Include(x => x.DepositSettlements)
                .Where(row => occupancyIds.Contains(row.OccupancyId) && row.Period == requestedPeriod && row.Kind == "Monthly")
                .ToDictionaryAsync(row => row.OccupancyId, cancellationToken);
            var result = rows.Select(row =>
            {
                invoices.TryGetValue(row.Reading.OccupancyId, out var invoice);
                return new ReadingDto(row.Reading.Id, row.Room.Id, row.Reading.OccupancyId, row.Room.Name, row.Building.Name,
                    row.Reading.Period, row.Reading.PreviousReading, row.Reading.CurrentReading, row.Reading.Consumption,
                    row.Reading.RecordedAt, invoice is null ? null : InvoiceService.ToDto(invoice), invoice?.LifecycleState.ToString() ?? "Issued");
            }).ToList();
            return Results.Ok(result);
        });

        routes.MapGet("/readings/submissions", async (string? period, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var requestedPeriod = period is null ? VietnamCalendar.CurrentVietnamPeriod() : ApiSupport.NormalizePeriod(period);
            var rows = await db.TenantMeterSubmissions.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Period == requestedPeriod)
                .OrderByDescending(x => x.SubmittedAt)
                .ThenBy(x => x.Id)
                .Select(x => new TenantMeterSubmissionDto(
                    x.Id,
                    x.RoomId,
                    x.OccupancyId,
                    x.Period,
                    x.PreviousReading,
                    x.SubmittedReading,
                    x.ApprovedReading,
                    x.TenantNote,
                    x.RejectReason,
                    x.Status.ToString(),
                    x.PhotoFileName,
                    x.PhotoContentType,
                    x.PhotoSizeBytes,
                    x.SubmittedAt,
                    x.ReviewedAt,
                    x.ReadingId,
                    x.InvoiceId))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        routes.MapGet("/readings/submissions/{id:guid}/photo", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var row = await db.TenantMeterSubmissions.AsNoTracking()
                .Where(x => x.Id == id && x.OwnerId == ownerId)
                .Select(x => new { x.PhotoFileName, x.PhotoContentType, x.PhotoContent })
                .SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy ảnh chụp đồng hồ điện.");

            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(row.PhotoFileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Bytes(row.PhotoContent, row.PhotoContentType);
        });

        routes.MapPost("/readings/submissions/{id:guid}/approve", async (
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveMeterSubmissionRequest? request,
            HttpContext context,
            AppDbContext db,
            InvoiceService invoiceService,
            [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null,
            CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var submission = await db.TenantMeterSubmissions
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chỉ số điện do khách gửi.");
            if (submission.Status == TenantMeterSubmissionStatus.Approved)
                throw new ApiException(StatusCodes.Status409Conflict, "Chỉ số điện này đã được duyệt.");

            var effectiveReading = request?.CurrentReading ?? submission.SubmittedReading;
            var work = await PrepareReadingAsync(db, ownerId, new ReadingRequest(submission.RoomId, submission.Period, effectiveReading), cancellationToken);
            if (work.Occupancy.Id != submission.OccupancyId)
                throw new ApiException(StatusCodes.Status409Conflict, "Lượt thuê của phòng đã thay đổi; không thể duyệt chỉ số điện cũ.");

            var now = DateTimeOffset.UtcNow;
            var reading = new MonthlyReading
            {
                OccupantsCount = work.Occupancy.People,
                RoomId = work.Room.Id,
                OccupancyId = work.Occupancy.Id,
                Period = work.Period,
                PreviousReading = work.PreviousReading,
                CurrentReading = work.CurrentReading,
                Consumption = work.Consumption,
                MonthlyRentRate = work.Room.Rent,
                ElectricityRate = work.Room.ElectricityRate,
                WaterRate = work.Room.WaterFee,
                WaterPerPerson = work.Room.WaterPerPerson,
                ServiceRate = work.Room.ServiceFee,
                BillingStartDate = work.BillingStartDate,
                BillingEndDate = work.BillingEndDate,
                BillableDays = work.BillableDays,
                MonthDays = work.MonthDays,
                RecordedAt = now
            };
            db.Readings.Add(reading);
            await db.SaveChangesAsync(cancellationToken);

            await invoiceService.GenerateForOwnerAsync(ownerId, work.Period, cancellationToken);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements)
                .SingleAsync(x => x.OccupancyId == work.Occupancy.Id && x.Period == work.Period && x.Kind == "Monthly", cancellationToken);

            submission.Status = TenantMeterSubmissionStatus.Approved;
            submission.ApprovedReading = effectiveReading;
            submission.ReviewedAt = now;
            submission.ReadingId = reading.Id;
            submission.InvoiceId = invoice.Id;
            submission.RejectReason = null;

            var outbox = new MilestoneOutboxEvent
            {
                OwnerId = ownerId,
                EventType = "InvoiceIssued",
                TargetId = $"{invoice.Id}:Owner",
                OccurredAt = invoice.IssuedAt,
                Status = "Pending"
            };
            db.MilestoneOutboxEvents.Add(outbox);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (milestoneTracker != null)
            {
                try
                {
                    await milestoneTracker.TrackInvoiceIssuedAsync(ownerId, invoice.Id, invoice.Kind, ActorType.Owner, invoice.IssuedAt, cancellationToken);
                    await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "ReadingRecorded", cancellationToken);
                    var milestoneRecorded = await db.OwnerMilestones.AnyAsync(m => m.OwnerId == ownerId && m.ActivatedAt != null, cancellationToken);
                    if (milestoneRecorded)
                    {
                        outbox.Status = "Completed";
                        outbox.ProcessedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
                catch { /* Telemetry failure must not block product; outbox remains Pending for durable retry */ }
            }

            return Results.Ok(PaymentEndpoints.ToSubmissionDto(submission));
        });

        routes.MapPost("/readings/submissions/{id:guid}/reject", async (
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectMeterSubmissionRequest? request,
            HttpContext context,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var submission = await db.TenantMeterSubmissions
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chỉ số điện do khách gửi.");
            if (submission.Status == TenantMeterSubmissionStatus.Approved)
                throw new ApiException(StatusCodes.Status409Conflict, "Chỉ số điện này đã được duyệt, không thể từ chối.");

            submission.Status = TenantMeterSubmissionStatus.Rejected;
            submission.RejectReason = OwnerManagementEndpoints.CleanOptional(request?.Reason, 500, "Lý do từ chối")
                ?? "Ảnh đồng hồ điện chưa rõ hoặc chỉ số chưa khớp, vui lòng chụp và gửi lại.";
            submission.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(PaymentEndpoints.ToSubmissionDto(submission));
        });

        routes.MapPost("/readings/preview", async (ReadingRequest request, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var work = await PrepareReadingAsync(db, ownerId, request, ct);
            var rent = ApiSupport.RoundVnd(work.Room.Rent);
            var electricity = ApiSupport.RoundVnd(work.Consumption * work.Room.ElectricityRate);
            var water = ApiSupport.RoundVnd(work.Room.WaterPerPerson ? work.Room.WaterFee * work.Occupancy.People : work.Room.WaterFee);
            var service = ApiSupport.RoundVnd(work.Room.ServiceFee);
            var total = InvoiceService.ValidateTotal(rent, electricity, water, service);
            return Results.Ok(new ReadingPreviewDto(work.Room.Id, work.Occupancy.Id, work.Period, work.BillingStartDate,
                work.BillingEndDate, work.BillableDays, work.MonthDays, rent, work.PreviousReading, work.CurrentReading,
                work.Consumption, work.Room.ElectricityRate, electricity, work.Room.WaterFee, work.Room.WaterPerPerson,
                water, work.Room.ServiceFee, service, total));
        });

        routes.MapPost("/readings", async (ReadingRequest request, HttpContext context, AppDbContext db, InvoiceService invoiceService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var work = await PrepareReadingAsync(db, ownerId, request, cancellationToken);
            var reading = new MonthlyReading
            {
                OccupantsCount = work.Occupancy.People,
                RoomId = work.Room.Id,
                OccupancyId = work.Occupancy.Id,
                Period = work.Period,
                PreviousReading = work.PreviousReading,
                CurrentReading = work.CurrentReading,
                Consumption = work.Consumption,
                MonthlyRentRate = work.Room.Rent,
                ElectricityRate = work.Room.ElectricityRate,
                WaterRate = work.Room.WaterFee,
                WaterPerPerson = work.Room.WaterPerPerson,
                ServiceRate = work.Room.ServiceFee,
                BillingStartDate = work.BillingStartDate,
                BillingEndDate = work.BillingEndDate,
                BillableDays = work.BillableDays,
                MonthDays = work.MonthDays,
                RecordedAt = DateTimeOffset.UtcNow
            };
            db.Readings.Add(reading);
            await db.SaveChangesAsync(cancellationToken);
            Invoice invoice;
            await invoiceService.GenerateForOwnerAsync(ownerId, work.Period, cancellationToken);
            invoice = await db.Invoices.Include(x => x.DepositSettlements)
                .SingleAsync(x => x.OccupancyId == work.Occupancy.Id && x.Period == work.Period && x.Kind == "Monthly", cancellationToken);
            var outbox = new MilestoneOutboxEvent
            {
                OwnerId = ownerId,
                EventType = "InvoiceIssued",
                TargetId = $"{invoice.Id}:Owner",
                OccurredAt = invoice.IssuedAt,
                Status = "Pending"
            };
            db.MilestoneOutboxEvents.Add(outbox);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try
                {
                    await milestoneTracker.TrackInvoiceIssuedAsync(ownerId, invoice.Id, invoice.Kind, ActorType.Owner, invoice.IssuedAt, cancellationToken);
                    await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "ReadingRecorded", cancellationToken);
                    var milestoneRecorded = await db.OwnerMilestones.AnyAsync(m => m.OwnerId == ownerId && m.ActivatedAt != null, cancellationToken);
                    if (milestoneRecorded)
                    {
                        outbox.Status = "Completed";
                        outbox.ProcessedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
                catch { /* Telemetry failure must not block product; outbox remains Pending for durable retry */ }
            }
            return Results.Created($"/api/readings?period={work.Period}", new ReadingDto(reading.Id, work.Room.Id, work.Occupancy.Id,
                work.Room.Name, work.Building.Name, reading.Period, reading.PreviousReading, reading.CurrentReading,
                reading.Consumption, reading.RecordedAt, InvoiceService.ToDto(invoice), invoice.LifecycleState.ToString()));
        });
    }
}
