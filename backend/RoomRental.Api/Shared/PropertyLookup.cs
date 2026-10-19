using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public static class PropertyLookup
{
    public static async Task<Building> FindBuildingAsync(AppDbContext db, Guid ownerId, Guid buildingId, CancellationToken cancellationToken)
    {
        var building = await db.Buildings.SingleOrDefaultAsync(row => row.Id == buildingId && row.OwnerId == ownerId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Building was not found.");
        if (building.ArchivedAt is not null)
            throw new ApiException(StatusCodes.Status409Conflict, "Dãy trọ đã được lưu trữ. Khôi phục dãy trước khi tạo phòng.");
        return building;
    }

    public static async Task<(Room Room, Building Building)> FindRoomAsync(AppDbContext db, Guid ownerId, Guid roomId,
        CancellationToken cancellationToken, bool allowArchived = false)
    {
        var row = await (from room in db.Rooms
                         join building in db.Buildings on room.BuildingId equals building.Id
                         where room.Id == roomId && building.OwnerId == ownerId
                         select new { Room = room, Building = building }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) throw new ApiException(StatusCodes.Status404NotFound, "Room was not found.");
        if (!allowArchived && row.Building.ArchivedAt is not null)
            throw new ApiException(StatusCodes.Status409Conflict, "Dãy trọ đã được lưu trữ. Khôi phục dãy trước khi tiếp tục.");
        if (!allowArchived && row.Room.ArchivedAt is not null)
            throw new ApiException(StatusCodes.Status409Conflict, "Phòng đã được lưu trữ. Khôi phục phòng trước khi tiếp tục.");
        return (row.Room, row.Building);
    }

}
