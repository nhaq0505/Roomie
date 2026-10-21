using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;

namespace RoomRental.Api.Services;

internal static class QueryAggregates
{
    internal static Task<decimal> SumMoneyAsync(AppDbContext db, IQueryable<decimal> values, CancellationToken ct) =>
        values.SumAsync(ct);

    internal static async Task<decimal> MaxReadingAsync(AppDbContext db, IQueryable<decimal> values, CancellationToken ct) =>
        await values.Select(x => (decimal?)x).MaxAsync(ct) ?? 0m;
}
