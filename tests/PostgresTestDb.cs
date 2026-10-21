using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomRental.Api.Data;

namespace RoomRental.Tests;

public sealed class PostgresTestDb : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    public string ConnectionString { get; }
    public string DatabaseName { get; }
    public string AdminConnectionString { get; }

    private PostgresTestDb(string adminConnStr, string connStr, string dbName)
    {
        AdminConnectionString = adminConnStr;
        ConnectionString = connStr;
        DatabaseName = dbName;
    }

    public static async Task<PostgresTestDb> CreateAsync(bool migrate = false, bool ensureCreated = false)
    {
        var rawConn = Environment.GetEnvironmentVariable("TEST_HTTP_POSTGRES_CONNECTION");

        if (string.IsNullOrWhiteSpace(rawConn))
        {
            throw new InvalidOperationException("TEST_HTTP_POSTGRES_CONNECTION environment variable must be set for PostgreSQL tests.");
        }

        var builder = new NpgsqlConnectionStringBuilder(rawConn);

        // Guard: loopback host only
        var host = builder.Host;
        if (host is not ("127.0.0.1" or "localhost" or "::1"))
        {
            throw new InvalidOperationException($"PostgreSQL QA tests require loopback host (127.0.0.1/localhost/::1), got '{host}'.");
        }

        // Guard: never touch app DB or local PostgreSQL at 15432
        if (builder.Port == 15432)
        {
            throw new InvalidOperationException("Safety guard: tests must never connect to port 15432 (app DB).");
        }

        // Guard: database template/admin name must be QA-only
        var initialDb = builder.Database ?? "";
        if (!initialDb.StartsWith("roomie_qa_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Safety guard: connection database '{initialDb}' must start with 'roomie_qa_'.");
        }

        builder.Pooling = false;

        var adminConnStr = builder.ConnectionString;
        var randomDbName = "roomie_qa_" + Guid.NewGuid().ToString("N");

        await CreateDatabaseAsync(adminConnStr, randomDbName);

        builder.Database = randomDbName;
        var testConnStr = builder.ConnectionString;

        var testDb = new PostgresTestDb(adminConnStr, testConnStr, randomDbName);

        try
        {
            if (migrate)
            {
                await using var db = testDb.CreatePostgresDbContext();
                await db.Database.MigrateAsync();
            }
            else if (ensureCreated)
            {
                await using var db = testDb.CreateDbContext();
                await db.Database.EnsureCreatedAsync();
            }
        }
        catch
        {
            await DropDatabaseAsync(adminConnStr, testConnStr, randomDbName);
            throw;
        }

        return testDb;
    }

    public static PostgresTestDb CreateSync(bool migrate = false, bool ensureCreated = false) =>
        CreateAsync(migrate, ensureCreated).GetAwaiter().GetResult();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public PostgresAppDbContext CreatePostgresDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new PostgresAppDbContext(options);
    }

    public NpgsqlConnection CreateConnection() => new(ConnectionString);

    public NpgsqlConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task CreateDatabaseAsync(string adminConnStr, string dbName)
    {
        await using var admin = new NpgsqlConnection(adminConnStr);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnStr, string testConnStr, string dbName)
    {
        if (!dbName.StartsWith("roomie_qa_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Safety guard: refusing to drop non-QA database '{dbName}'.");
        }

        try
        {
            using var poolConn = new NpgsqlConnection(testConnStr);
            NpgsqlConnection.ClearPool(poolConn);
        }
        catch
        {
            // Ignore pool clear failure
        }

        await using var admin = new NpgsqlConnection(adminConnStr);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE \"{dbName}\" WITH (FORCE)", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void DropDatabaseSync(string adminConnStr, string testConnStr, string dbName) =>
        DropDatabaseAsync(adminConnStr, testConnStr, dbName).GetAwaiter().GetResult();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DropDatabaseSync(AdminConnectionString, ConnectionString, DatabaseName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DropDatabaseAsync(AdminConnectionString, ConnectionString, DatabaseName);
    }
}
