using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

internal static class MigrationTests
{
    public static async Task RunAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteAppDbContext(options);
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261015043532_InitialCreate");
        var retainedId=Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"Users\" (\"Id\",\"Name\",\"Email\",\"PasswordHash\",\"Role\",\"Status\",\"SubscriptionTier\",\"CreatedAt\") VALUES ({0}, 'Existing user', 'retained@example.test', 'fixture', 'Owner', 'Approved', 'Free', 0)", retainedId);
        await db.Database.MigrateAsync();
        if (!(await db.Users.SingleAsync()).Id.Equals(retainedId)) throw new Exception("Additive migration lost existing data");
        db.Users.Remove(await db.Users.SingleAsync()); await db.SaveChangesAsync();
        if ((await db.Database.GetAppliedMigrationsAsync()).Count() != db.Database.GetMigrations().Count() || db.Database.HasPendingModelChanges())
            throw new Exception("SQLite initial migration or model snapshot is incomplete.");

        var user = new AppUser { Email = "migration@example.test", Name = "Migration fixture", PasswordHash = "fixture" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        if (await db.Users.CountAsync() != 1 || (await db.Users.SingleAsync()).Id != user.Id)
            throw new Exception("Repeated migration must preserve existing data.");

        foreach (var index in new[] { "IX_Invoices_OccupancyId_Deposit", "IX_Invoices_OccupancyId_FinalMeter",
                     "IX_Invoices_OccupancyId_Period", "IX_PayOsAttempts_InvoiceId_Pending" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name=$name";
            command.Parameters.AddWithValue("$name", index);
            var sql = Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
            if (!sql.Contains("UNIQUE") || !sql.Contains("WHERE"))
                throw new Exception($"Migration lost filtered unique index {index}.");
        }

        // A legacy database is deliberately rejected, never silently erased or baselined.
        await using var legacyConnection = new SqliteConnection("Data Source=:memory:");
        await legacyConnection.OpenAsync();
        await using var legacy = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(legacyConnection).Options);
        await legacy.Database.EnsureCreatedAsync();
        legacy.Users.Add(new AppUser { Email = "legacy@example.test", Name = "Keep until explicit reset", PasswordHash = "fixture" });
        await legacy.SaveChangesAsync();
        try
        {
            await legacy.Database.MigrateAsync();
            throw new Exception("A legacy schema must require explicit reset.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("already exists")) { }
        if (await legacy.Users.CountAsync() != 1)
            throw new Exception("Failed migration changed legacy data.");
        Console.WriteLine("PASS migrations: fresh SQLite schema, snapshot, filtered indexes, repeat apply, legacy data protection");
    }
}
