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
    private static void MapReadingsRoutes(RouteGroupBuilder routes)
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
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "ReadingRecorded", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Created($"/api/readings?period={work.Period}", new ReadingDto(reading.Id, work.Room.Id, work.Occupancy.Id,
                work.Room.Name, work.Building.Name, reading.Period, reading.PreviousReading, reading.CurrentReading,
                reading.Consumption, reading.RecordedAt, InvoiceService.ToDto(invoice), invoice.LifecycleState.ToString()));
        });

    }
}
