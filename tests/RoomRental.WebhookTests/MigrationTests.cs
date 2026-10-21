using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Tests;

internal static class MigrationTests
{
    public static async Task RunAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(migrate: false, ensureCreated: false);
        await using var db = fixture.CreatePostgresDbContext();
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261015043550_InitialCreate");

        var retainedId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Users\" (\"Id\",\"Name\",\"Email\",\"PasswordHash\",\"Role\",\"Status\",\"SubscriptionTier\",\"CreatedAt\") VALUES ({0}, 'Existing user', 'retained@example.test', 'fixture', 'Owner', 'Approved', 'Free', NOW())",
            retainedId);

        await db.Database.MigrateAsync();
        if (!(await db.Users.SingleAsync()).Id.Equals(retainedId))
            throw new Exception("Additive migration lost existing data");

        db.Users.Remove(await db.Users.SingleAsync());
        await db.SaveChangesAsync();

        if ((await db.Database.GetAppliedMigrationsAsync()).Count() != db.Database.GetMigrations().Count() || db.Database.HasPendingModelChanges())
            throw new Exception("PostgreSQL initial migration or model snapshot is incomplete.");

        var user = new AppUser { Email = "migration@example.test", Name = "Migration fixture", PasswordHash = "fixture" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        if (await db.Users.CountAsync() != 1 || (await db.Users.SingleAsync()).Id != user.Id)
            throw new Exception("Repeated migration must preserve existing data.");

        var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var index in new[] { "IX_Invoices_OccupancyId_Deposit", "IX_Invoices_OccupancyId_FinalMeter",
                     "IX_Invoices_OccupancyId_Period", "IX_PayOsAttempts_InvoiceId_Pending" })
        {
            await using var command = new Npgsql.NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = @name", connection);
            command.Parameters.AddWithValue("name", index);
            var sql = Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
            if (!sql.Contains("UNIQUE") || !sql.Contains("WHERE"))
                throw new Exception($"Migration lost filtered unique index {index}.");
        }

        // A legacy database is deliberately rejected, never silently erased or baselined.
        await using var legacyFixture = await PostgresTestDb.CreateAsync(migrate: false, ensureCreated: false);
        await using var legacy = legacyFixture.CreatePostgresDbContext();
        await legacy.Database.EnsureCreatedAsync();
        legacy.Users.Add(new AppUser { Email = "legacy@example.test", Name = "Keep until explicit reset", PasswordHash = "fixture" });
        await legacy.SaveChangesAsync();
        try
        {
            await legacy.Database.MigrateAsync();
            throw new Exception("A legacy schema must require explicit reset.");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07" || ex.Message.Contains("already exists")) { }
        if (await legacy.Users.CountAsync() != 1)
            throw new Exception("Failed migration changed legacy data.");

        Console.WriteLine("PASS migrations: fresh PostgreSQL schema, snapshot, filtered indexes, repeat apply, legacy data protection");
    }
}
