using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class ExpenseEndpoints
{
    private const string RecurringProMessage = "Chi định kỳ dành cho gói Pro. Vui lòng nâng cấp để sử dụng.";

    private static void MapRecurringRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("", async (HttpContext context, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var rows = await db.RecurringExpenses.AsNoTracking().Where(row => row.OwnerId == ownerId && row.DeletedAt == null)
                .OrderBy(row => row.DayOfMonth).ThenBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync(ct);
            return Results.Ok(await ToRecurringDtosAsync(db, ownerId, rows, clock.GetUtcNow(), ct));
        });

        routes.MapPost("", async (RecurringExpenseRequest request, HttpContext context, AppDbContext db, TimeProvider clock,
            RecurringExpenseGenerator generator, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            await ExpenseSupport.EnsureProAsync(db, ownerId, now, ct, RecurringProMessage);
            var key = ExpenseSupport.NormalizeIdempotencyKey(context.Request.Headers["Idempotency-Key"].FirstOrDefault());
            var normalized = NormalizeRecurring(request);
            var fingerprint = RequestFingerprint.Fingerprint(new
            {
                normalized.Category, request.Amount, normalized.Description, request.BuildingId, request.RoomId,
                request.DayOfMonth, normalized.StartPeriod, normalized.EndPeriod
            });
            if (key is not null)
            {
                var replay = await FindRecurringReplayAsync(db, ownerId, key, fingerprint, ct);
                if (replay is not null) return Results.Ok((await ToRecurringDtosAsync(db, ownerId, [replay], now, ct))[0]);
            }
            var (buildingId, roomId) = await ExpenseSupport.ResolveLocationAsync(db, ownerId, request.BuildingId, request.RoomId, null, null, ct);

            var template = new RecurringExpense
            {
                OwnerId = ownerId, BuildingId = buildingId, RoomId = roomId, Category = normalized.Category, Amount = request.Amount,
                Description = normalized.Description, DayOfMonth = request.DayOfMonth, StartPeriod = normalized.StartPeriod,
                EndPeriod = normalized.EndPeriod, IdempotencyKey = key, RequestFingerprint = fingerprint, CreatedAt = now, UpdatedAt = now
            };
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                db.RecurringExpenses.Add(template);
                await db.SaveChangesAsync(ct);
                await generator.GenerateForTemplateAsync(template, ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException exception) when (key is not null && ExpenseSupport.IsUniqueViolation(exception))
            {
                db.ChangeTracker.Clear();
                var winner = await FindRecurringReplayAsync(db, ownerId, key, fingerprint, ct)
                    ?? throw new ApiException(StatusCodes.Status409Conflict, "Another request changed this record. Refresh and retry.");
                return Results.Ok((await ToRecurringDtosAsync(db, ownerId, [winner], now, ct))[0]);
            }
            return Results.Created($"/api/recurring-expenses/{template.Id}", (await ToRecurringDtosAsync(db, ownerId, [template], now, ct))[0]);
        });

        routes.MapPut("/{id:guid}", async (Guid id, RecurringExpenseRequest request, HttpContext context, AppDbContext db,
            TimeProvider clock, RecurringExpenseGenerator generator, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            await ExpenseSupport.EnsureProAsync(db, ownerId, now, ct, RecurringProMessage);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var template = await LoadTemplateAsync(db, ownerId, id, request.ExpectedVersion, ct);
            var normalized = NormalizeRecurring(request);
            var (buildingId, roomId) = await ExpenseSupport.ResolveLocationAsync(db, ownerId, request.BuildingId, request.RoomId,
                template.BuildingId, template.RoomId, ct);
            template.Category = normalized.Category;
            template.Amount = request.Amount;
            template.Description = normalized.Description;
            template.BuildingId = buildingId;
            template.RoomId = roomId;
            template.DayOfMonth = request.DayOfMonth;
            template.StartPeriod = normalized.StartPeriod;
            template.EndPeriod = normalized.EndPeriod;
            template.Version++;
            template.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            // Edits apply to periods not yet generated; already materialized expenses keep their recorded values.
            if (template.PausedAt is null) await generator.GenerateForTemplateAsync(template, ct);
            await transaction.CommitAsync(ct);
            return Results.Ok((await ToRecurringDtosAsync(db, ownerId, [template], now, ct))[0]);
        });

        routes.MapPost("/{id:guid}/pause", async (Guid id, RecurringExpenseStateRequest request, HttpContext context, AppDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            var template = await LoadTemplateAsync(db, ownerId, id, request.ExpectedVersion, ct);
            if (template.PausedAt is null)
            {
                template.PausedAt = now;
                template.Version++;
                template.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok((await ToRecurringDtosAsync(db, ownerId, [template], now, ct))[0]);
        });

        routes.MapPost("/{id:guid}/resume", async (Guid id, RecurringExpenseStateRequest request, HttpContext context, AppDbContext db,
            TimeProvider clock, RecurringExpenseGenerator generator, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            await ExpenseSupport.EnsureProAsync(db, ownerId, now, ct, RecurringProMessage);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var template = await LoadTemplateAsync(db, ownerId, id, request.ExpectedVersion, ct);
            if (template.PausedAt is not null)
            {
                await ExpenseSupport.ResolveLocationAsync(db, ownerId, template.BuildingId, template.RoomId, null, null, ct);
                template.PausedAt = null;
                template.Version++;
                template.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                await generator.GenerateForTemplateAsync(template, ct);
            }
            await transaction.CommitAsync(ct);
            return Results.Ok((await ToRecurringDtosAsync(db, ownerId, [template], now, ct))[0]);
        });

        routes.MapDelete("/{id:guid}", async (Guid id, long? expectedVersion, HttpContext context, AppDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            var template = await LoadTemplateAsync(db, ownerId, id, expectedVersion, ct);
            template.DeletedAt = now;
            template.UpdatedAt = now;
            template.Version++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true, id });
        });
    }

    private sealed record NormalizedRecurring(string Category, string? Description, string StartPeriod, string? EndPeriod);

    private static NormalizedRecurring NormalizeRecurring(RecurringExpenseRequest request)
    {
        var category = ExpenseSupport.NormalizeCategory(request.Category);
        ExpenseSupport.ValidateAmount(request.Amount);
        var description = ExpenseSupport.NormalizeDescription(request.Description);
        if (request.DayOfMonth is < 1 or > 28)
            throw new ApiException(StatusCodes.Status400BadRequest, "Ngày chi hàng tháng phải từ 1 đến 28.");
        var start = ApiSupport.NormalizePeriod(request.StartPeriod);
        var end = string.IsNullOrWhiteSpace(request.EndPeriod) ? null : ApiSupport.NormalizePeriod(request.EndPeriod);
        if (end is not null && string.CompareOrdinal(end, start) < 0)
            throw new ApiException(StatusCodes.Status400BadRequest, "Kỳ kết thúc phải từ kỳ bắt đầu trở đi.");
        return new NormalizedRecurring(category, description, start, end);
    }

    private static async Task<RecurringExpense> LoadTemplateAsync(AppDbContext db, Guid ownerId, Guid id, long? expectedVersion, CancellationToken ct)
    {
        if (expectedVersion is not long version)
            throw new ApiException(StatusCodes.Status400BadRequest, "expectedVersion is required.");
        var template = await db.RecurringExpenses.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId && row.DeletedAt == null, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy mẫu chi định kỳ.");
        if (template.Version != version)
            throw new ApiException(StatusCodes.Status409Conflict, "Mẫu chi định kỳ đã thay đổi. Tải lại rồi thử lại.");
        return template;
    }

    private static async Task<RecurringExpense?> FindRecurringReplayAsync(AppDbContext db, Guid ownerId, string key, string fingerprint, CancellationToken ct)
    {
        var prior = await db.RecurringExpenses.AsNoTracking().SingleOrDefaultAsync(row => row.OwnerId == ownerId && row.IdempotencyKey == key, ct);
        if (prior is null) return null;
        if (!string.Equals(prior.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new ApiException(StatusCodes.Status409Conflict, "This idempotency key was already used for a different recurring expense.");
        if (prior.DeletedAt is not null)
            throw new ApiException(StatusCodes.Status409Conflict, "Mẫu chi định kỳ của yêu cầu này đã bị xóa.");
        return prior;
    }

    private static async Task<List<RecurringExpenseDto>> ToRecurringDtosAsync(AppDbContext db, Guid ownerId,
        IReadOnlyList<RecurringExpense> rows, DateTimeOffset now, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var today = ApiSupport.VietnamDate(now);
        var isPro = await ExpenseSupport.IsProAsync(db, ownerId, now, ct);
        var (buildings, rooms) = await ExpenseSupport.LoadNamesAsync(db, ownerId, rows.Select(row => row.BuildingId), rows.Select(row => row.RoomId), ct);
        var buildingIds = rows.Where(row => row.BuildingId.HasValue).Select(row => row.BuildingId!.Value).Distinct().ToList();
        var roomIds = rows.Where(row => row.RoomId.HasValue).Select(row => row.RoomId!.Value).Distinct().ToList();
        var archivedBuildings = buildingIds.Count == 0 ? [] : await db.Buildings.AsNoTracking()
            .Where(row => row.OwnerId == ownerId && buildingIds.Contains(row.Id) && row.ArchivedAt != null).Select(row => row.Id).ToListAsync(ct);
        var archivedRooms = roomIds.Count == 0 ? [] : await db.Rooms.AsNoTracking()
            .Where(row => roomIds.Contains(row.Id) && row.ArchivedAt != null).Select(row => row.Id).ToListAsync(ct);
        var templateIds = rows.Select(row => row.Id).ToList();
        var lastPeriods = await db.Expenses.AsNoTracking()
            .Where(row => row.OwnerId == ownerId && row.RecurringExpenseId != null && templateIds.Contains(row.RecurringExpenseId.Value))
            .GroupBy(row => row.RecurringExpenseId!.Value)
            .Select(group => new { group.Key, Last = group.Max(row => row.RecurrencePeriod) })
            .ToDictionaryAsync(row => row.Key, row => row.Last, ct);
        return rows.Select(row =>
        {
            var archived = (row.BuildingId is Guid b && archivedBuildings.Contains(b)) || (row.RoomId is Guid r && archivedRooms.Contains(r));
            return new RecurringExpenseDto(row.Id, row.Category, row.Amount, row.Description,
                row.BuildingId, row.BuildingId is Guid bid ? buildings.GetValueOrDefault(bid) : null,
                row.RoomId, row.RoomId is Guid rid ? rooms.GetValueOrDefault(rid) : null,
                row.DayOfMonth, row.StartPeriod, row.EndPeriod, row.PausedAt is not null,
                RecurringExpenseGenerator.SkipReason(row, isPro, archived, today), lastPeriods.GetValueOrDefault(row.Id),
                row.Version, row.CreatedAt);
        }).ToList();
    }
}
