using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

internal static class SharedSupportTests
{
    public static async Task RunAsync()
    {
        // Existing persisted request fingerprints must survive the helper extraction.
        var fingerprint = RequestFingerprint.Fingerprint(new { Amount = 30000, Note = "fixture" });
        if (fingerprint != "830C6CC555CA1F7ED86D5CF928E5E8FB28C6929CC6E990A8F227C53DE82FED38")
            throw new Exception("Request fingerprint no longer matches the existing web JSON contract");
        if (fingerprint == RequestFingerprint.Fingerprint(new { Amount = 30001, Note = "fixture" }))
            throw new Exception("Different financial requests must not share a fingerprint");

        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new AppUser { Name = "Fixture", Email = "shared@example.invalid", PasswordHash = "fixture" };
        var building = new Building { OwnerId = owner.Id, Name = "Fixture building" };
        var room = new Room { BuildingId = building.Id, Name = "Fixture room" };
        db.Users.Add(owner); db.Buildings.Add(building); db.Rooms.Add(room);
        await db.SaveChangesAsync();
        var found = await PropertyLookup.FindRoomAsync(db, owner.Id, room.Id, default);
        if (found.Room.Id != room.Id || found.Building.Id != building.Id) throw new Exception("Room lookup lost its building association");
        await RejectsAsync(404, () => PropertyLookup.FindRoomAsync(db, Guid.NewGuid(), room.Id, default));
        await RejectsAsync(404, () => PropertyLookup.FindBuildingAsync(db, Guid.NewGuid(), building.Id, default));
        room.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await RejectsAsync(409, () => PropertyLookup.FindRoomAsync(db, owner.Id, room.Id, default));
        _ = await PropertyLookup.FindRoomAsync(db, owner.Id, room.Id, default, allowArchived: true);
        room.ArchivedAt = null; building.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await RejectsAsync(409, () => PropertyLookup.FindRoomAsync(db, owner.Id, room.Id, default));
        await RejectsAsync(409, () => PropertyLookup.FindBuildingAsync(db, owner.Id, building.Id, default));
        _ = await PropertyLookup.FindRoomAsync(db, owner.Id, room.Id, default, allowArchived: true);
        await RejectsAsync(404, () => PropertyLookup.FindRoomAsync(db, Guid.NewGuid(), room.Id, default, allowArchived: true));
        Console.WriteLine("PASS: extracted fingerprint compatibility; room/building owner isolation and archive guards");
    }

    private static async Task RejectsAsync(int status, Func<Task> action)
    {
        try { await action(); }
        catch (ApiException exception) when (exception.StatusCode == status) { return; }
        throw new Exception($"Expected lookup rejection {status}");
    }
}
