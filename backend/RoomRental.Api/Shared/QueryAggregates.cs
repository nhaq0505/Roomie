using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;

namespace RoomRental.Api.Services;

internal static class QueryAggregates
{
    internal static async Task<decimal> SumMoneyAsync(AppDbContext db, IQueryable<decimal> values, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) return await values.SumAsync(ct);
        // Preserve exact decimal money on SQLite, whose native SUM uses floating point.
        return (await values.ToListAsync(ct)).Sum();
    }

    internal static async Task<decimal> MaxReadingAsync(AppDbContext db, IQueryable<decimal> values, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) return await values.Select(x => (decimal?)x).MaxAsync(ct) ?? 0m;
        // Meter readings are bounded to 1e9 and 3 decimal places: safely within double's precision.
        return (decimal)(await values.Select(x => (double?)x).MaxAsync(ct) ?? 0d);
    }
}
