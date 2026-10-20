using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapRoomsRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/rooms", async (string? period, bool? includeArchived, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var readingPeriod = period is null ? VietnamCalendar.CurrentVietnamPeriod() : ApiSupport.NormalizePeriod(period);
            var rows = await (from room in db.Rooms.AsNoTracking()
                              join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                              where building.OwnerId == ownerId && (includeArchived == true || (room.ArchivedAt == null && building.ArchivedAt == null))
                              orderby building.Name, room.Name, room.Id
                              select new { Room = room, Building = building }).ToListAsync(cancellationToken);
            var roomIds = rows.Select(row => row.Room.Id).ToList();
            var activeOccupancies = await db.Occupancies.AsNoTracking()
                .Where(row => row.IsActive && roomIds.Contains(row.RoomId))
                .ToListAsync(cancellationToken);
            var occupancyByRoom = activeOccupancies.ToDictionary(row => row.RoomId);
            var allReadings = roomIds.Count == 0
                ? []
                : await db.Readings.AsNoTracking()
                    .Where(row => roomIds.Contains(row.RoomId) && (row.Period == readingPeriod ||
                        row.Id == db.Readings.Where(prior => prior.RoomId == row.RoomId && string.Compare(prior.Period, readingPeriod) < 0)
                            .OrderByDescending(prior => prior.Period).ThenByDescending(prior => prior.RecordedAt)
                            .Select(prior => prior.Id).FirstOrDefault()))
                    .OrderByDescending(row => row.Period).ThenByDescending(row => row.RecordedAt)
                    .ToListAsync(cancellationToken);
            var readingsByRoom = allReadings.GroupBy(row => row.RoomId).ToDictionary(g => g.Key, g => (IReadOnlyList<MonthlyReading>)g.ToList());
            // Aggregate physical meter maxima independently of the selected period/occupancy.
            var meterMaxima = await db.Readings.AsNoTracking().Where(x => roomIds.Contains(x.RoomId))
                .GroupBy(x => x.RoomId).Select(g => new { RoomId = g.Key, Maximum = g.Max(x => (double)x.CurrentReading) })
                .ToDictionaryAsync(x => x.RoomId, x => (decimal)x.Maximum, cancellationToken);
            var maxFinalByRoom = await db.Invoices.AsNoTracking()
                .Where(x => roomIds.Contains(x.RoomId) && x.IsFinalMeterReading)
                .GroupBy(x => x.RoomId).Select(g => new { RoomId = g.Key, Maximum = g.Max(x => (double)x.CurrentReading) })
                .ToDictionaryAsync(x => x.RoomId, x => (decimal)x.Maximum, cancellationToken);
            var activeOccupancyIds = activeOccupancies.Select(x => x.Id).ToList();
            var deposits = activeOccupancyIds.Count == 0
                ? []
                : await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking()
                    .Where(x => activeOccupancyIds.Contains(x.OccupancyId) && x.Kind == "Deposit")
                    .ToListAsync(cancellationToken);
            var depositByOccupancy = deposits.ToDictionary(x => x.OccupancyId);

            // Read verification once per owner, not once for every room.
            var canShareTenantLink = await tokenService.CanShareAsync(ownerId, webhookService, cancellationToken);
            var result = new List<object>(rows.Count);
            foreach (var row in rows)
            {
                occupancyByRoom.TryGetValue(row.Room.Id, out var occupancy);
                var roomReadings = readingsByRoom.GetValueOrDefault(row.Room.Id) ?? [];
                var maxFinal = maxFinalByRoom.GetValueOrDefault(row.Room.Id, 0m);
                var deposit = occupancy is not null ? depositByOccupancy.GetValueOrDefault(occupancy.Id) : null;
                var dto = BuildRoomDto(tokenService, row.Room, row.Building, occupancy, readingPeriod, roomReadings, maxFinal, deposit, canShareTenantLink, meterMaxima.GetValueOrDefault(row.Room.Id, row.Room.InitialReading));
                result.Add(new { dto.Id, dto.BuildingId, dto.BuildingName, dto.Name, dto.Rent, dto.ElectricityRate, dto.WaterFee,
                    dto.WaterPerPerson, dto.ServiceFee, dto.InitialReading, dto.PreviousReading, dto.Occupancy,
                    isArchived = row.Room.ArchivedAt is not null, archivedAt = row.Room.ArchivedAt,
                    archiveBatchId = row.Room.ArchiveBatchId, buildingArchived = row.Building.ArchivedAt is not null });
            }
            return Results.Ok(result);
        });

        routes.MapPost("/rooms", async (RoomCreateRequest request, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var owner = await db.Users.SingleOrDefaultAsync(u => u.Id == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
            if (!owner.IsPro)
            {
                var activeRooms = await (from r in db.Rooms
                                         join b in db.Buildings on r.BuildingId equals b.Id
                                         where b.OwnerId == ownerId && r.ArchivedAt == null && b.ArchivedAt == null
                                         select r.Id).CountAsync(cancellationToken);
                if (activeRooms >= AppUser.FreeTierMaxRooms)
                    throw new ApiException(StatusCodes.Status403Forbidden,
                        $"Gói Miễn phí chỉ được tạo tối đa {AppUser.FreeTierMaxRooms} phòng. Vui lòng nâng cấp tài khoản Pro để thêm phòng.");
            }
            var building = await PropertyLookup.FindBuildingAsync(db, ownerId, request.BuildingId, cancellationToken);
            ValidateFees(request.Name, request.Rent, request.ElectricityRate, request.WaterFee, request.ServiceFee, request.InitialReading);
            var room = new Room
            {
                BuildingId = building.Id,
                Name = request.Name.Trim(),
                Rent = ApiSupport.RoundVnd(request.Rent),
                ElectricityRate = ApiSupport.RoundVnd(request.ElectricityRate),
                WaterFee = ApiSupport.RoundVnd(request.WaterFee),
                WaterPerPerson = request.WaterPerPerson,
                ServiceFee = ApiSupport.RoundVnd(request.ServiceFee),
                InitialReading = request.InitialReading
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "RoomCreate", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Created($"/api/rooms/{room.Id}", await ToRoomDtoAsync(db, tokenService, webhookService, room, building, null, VietnamCalendar.CurrentVietnamPeriod(), cancellationToken));
        });

        routes.MapGet("/rooms/{id:guid}", async (Guid id, string? period, bool? includeArchived, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var row = await PropertyLookup.FindRoomAsync(db, ApiSupport.OwnerId(context), id, cancellationToken, includeArchived == true);
            var occupancy = await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(item => item.RoomId == id && item.IsActive, cancellationToken);
            var readingPeriod = period is null ? VietnamCalendar.CurrentVietnamPeriod() : ApiSupport.NormalizePeriod(period);
            var dto = await ToRoomDtoAsync(db, tokenService, webhookService, row.Room, row.Building, occupancy, readingPeriod, cancellationToken);
            return Results.Ok(new { dto.Id, dto.BuildingId, dto.BuildingName, dto.Name, dto.Rent, dto.ElectricityRate, dto.WaterFee,
                dto.WaterPerPerson, dto.ServiceFee, dto.InitialReading, dto.PreviousReading, dto.Occupancy,
                isArchived = row.Room.ArchivedAt is not null, archivedAt = row.Room.ArchivedAt,
                archiveBatchId = row.Room.ArchiveBatchId, buildingArchived = row.Building.ArchivedAt is not null });
        });

        routes.MapMethods("/rooms/{id:guid}", ["PATCH"], async (Guid id, RoomUpdateRequest request, HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            ValidateFees(request.Name, request.Rent, request.ElectricityRate, request.WaterFee, request.ServiceFee, 0);
            room.Name = request.Name.Trim();
            room.Rent = ApiSupport.RoundVnd(request.Rent);
            room.ElectricityRate = ApiSupport.RoundVnd(request.ElectricityRate);
            room.WaterFee = ApiSupport.RoundVnd(request.WaterFee);
            room.WaterPerPerson = request.WaterPerPerson;
            room.ServiceFee = ApiSupport.RoundVnd(request.ServiceFee);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "RoomUpdate", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            var occupancy = await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(item => item.RoomId == id && item.IsActive, cancellationToken);
            return Results.Ok(await ToRoomDtoAsync(db, tokenService, webhookService, room, building, occupancy, VietnamCalendar.CurrentVietnamPeriod(), cancellationToken));
        });

        routes.MapDelete("/rooms/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var row = await (from room in db.Rooms
                             join building in db.Buildings on room.BuildingId equals building.Id
                             where room.Id == id && building.OwnerId == ownerId
                             select new { Room = room, Building = building }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Room was not found.");
            if (row.Room.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Phòng đã được lưu trữ.");
            if (row.Building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Không thể lưu trữ phòng khi dãy trọ đã được lưu trữ.");
            var blockReason = await RoomArchiveBlockReasonAsync(db, ownerId, row.Room.Id, cancellationToken);
            if (blockReason is not null)
                throw new ApiException(StatusCodes.Status409Conflict, $"Không thể lưu trữ phòng: {blockReason}");

            var batchId = Guid.NewGuid();
            row.Room.ArchivedAt = DateTimeOffset.UtcNow;
            row.Room.ArchivedBy = ownerId;
            row.Room.ArchiveBatchId = batchId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "RoomArchive", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Ok(new { archived = true, archiveBatchId = batchId });
        });

        routes.MapPost("/rooms/{id:guid}/restore", async (Guid id, HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var row = await (from room in db.Rooms
                             join building in db.Buildings on room.BuildingId equals building.Id
                             where room.Id == id && building.OwnerId == ownerId
                             select new { Room = room, Building = building }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Room was not found.");
            if (row.Room.ArchivedAt is null)
                throw new ApiException(StatusCodes.Status409Conflict, "Phòng chưa được lưu trữ.");
            if (row.Building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Cần khôi phục dãy trọ trước khi khôi phục phòng.");
            var owner = await db.Users.SingleOrDefaultAsync(u => u.Id == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
            if (!owner.IsPro)
            {
                var activeRooms = await (from r in db.Rooms
                                         join b in db.Buildings on r.BuildingId equals b.Id
                                         where b.OwnerId == ownerId && r.ArchivedAt == null && b.ArchivedAt == null
                                         select r.Id).CountAsync(cancellationToken);
                if (activeRooms >= AppUser.FreeTierMaxRooms)
                    throw new ApiException(StatusCodes.Status403Forbidden,
                        $"Gói Miễn phí chỉ được tạo tối đa {AppUser.FreeTierMaxRooms} phòng. Vui lòng nâng cấp tài khoản Pro để khôi phục phòng.");
            }
            row.Room.ArchivedAt = null;
            row.Room.ArchivedBy = null;
            row.Room.ArchiveBatchId = null;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "RoomRestore", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Ok(new { restored = true });
        });

    }
}
