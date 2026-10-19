using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapBuildingsRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/buildings", async (bool? includeArchived, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var rows = await db.Buildings.AsNoTracking().Where(row => row.OwnerId == ownerId &&
                    (includeArchived == true || row.ArchivedAt == null))
                .OrderBy(row => row.Name).ThenBy(row => row.Id)
                .Select(row => new { row.Id, row.Name, row.Address, isArchived = row.ArchivedAt != null,
                    archivedAt = row.ArchivedAt, archiveBatchId = row.ArchiveBatchId }).ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        routes.MapPost("/buildings", async (BuildingRequest request, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Building name is required and must be 160 characters or fewer.");
            var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            var building = new Building { OwnerId = ApiSupport.OwnerId(context), Name = name, Address = address is { Length: > 500 } ? address[..500] : address };
            db.Buildings.Add(building);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/buildings/{building.Id}", new BuildingDto(building.Id, building.Name, building.Address));
        });

        routes.MapMethods("/buildings/{id:guid}", ["PUT", "PATCH"], async (Guid id, BuildingRequest request, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var building = await db.Buildings.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Building was not found.");
            if (building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Dãy trọ đã được lưu trữ.");

            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Building name is required and must be 160 characters or fewer.");

            var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            building.Name = name;
            building.Address = address is { Length: > 500 } ? address[..500] : address;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new BuildingDto(building.Id, building.Name, building.Address));
        });

        routes.MapDelete("/buildings/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var building = await db.Buildings.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Building was not found.");
            if (building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Dãy trọ đã được lưu trữ.");

            var rooms = await db.Rooms.Where(room => room.BuildingId == building.Id && room.ArchivedAt == null)
                .OrderBy(room => room.Name).ThenBy(room => room.Id).ToListAsync(cancellationToken);
            foreach (var room in rooms)
            {
                var blockReason = await RoomArchiveBlockReasonAsync(db, ownerId, room.Id, cancellationToken);
                if (blockReason is not null)
                    throw new ApiException(StatusCodes.Status409Conflict,
                        $"Không thể lưu trữ dãy vì phòng {room.Name}: {blockReason}");
            }

            var archivedAt = DateTimeOffset.UtcNow;
            var batchId = Guid.NewGuid();
            building.ArchivedAt = archivedAt;
            building.ArchivedBy = ownerId;
            building.ArchiveBatchId = batchId;
            foreach (var room in rooms)
            {
                room.ArchivedAt = archivedAt;
                room.ArchivedBy = ownerId;
                room.ArchiveBatchId = batchId;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { archived = true, archiveBatchId = batchId });
        });

        routes.MapPost("/buildings/{id:guid}/restore", async (Guid id, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var building = await db.Buildings.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Building was not found.");
            if (building.ArchivedAt is null)
                throw new ApiException(StatusCodes.Status409Conflict, "Dãy trọ chưa được lưu trữ.");

            var batchId = building.ArchiveBatchId;
            building.ArchivedAt = null;
            building.ArchivedBy = null;
            building.ArchiveBatchId = null;
            var restoredRoomIds = batchId is Guid archiveBatchId
                ? await db.Rooms.Where(room => room.BuildingId == building.Id && room.ArchiveBatchId == archiveBatchId)
                    .Select(room => room.Id).ToListAsync(cancellationToken)
                : [];
            if (batchId is Guid batch)
            {
                var rooms = await db.Rooms.Where(room => room.BuildingId == building.Id && room.ArchiveBatchId == batch)
                    .ToListAsync(cancellationToken);
                foreach (var room in rooms)
                {
                    room.ArchivedAt = null;
                    room.ArchivedBy = null;
                    room.ArchiveBatchId = null;
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { restored = true, restoredRoomIds });
        });

    }
}
