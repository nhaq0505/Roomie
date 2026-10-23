using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class ExpenseEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static void MapExpenseEndpoints(this WebApplication app)
    {
        var expenses = app.MapGroup("/api/expenses").RequireAuthorization(policy => policy.RequireRole("Owner"));
        MapExpenseRoutes(expenses);
        MapAttachmentRoutes(expenses);
        var recurring = app.MapGroup("/api/recurring-expenses").RequireAuthorization(policy => policy.RequireRole("Owner"));
        MapRecurringRoutes(recurring);
    }

    private static void MapExpenseRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("", async (DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, Guid? roomId, string? category,
            int? page, int? pageSize, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (dateFrom is DateOnly from && dateTo is DateOnly to && from > to)
                throw new ApiException(StatusCodes.Status400BadRequest, "dateFrom must be on or before dateTo.");
            if (page is < 1 or > 1_000_000)
                throw new ApiException(StatusCodes.Status400BadRequest, "page must be between 1 and 1,000,000.");
            if (pageSize is < 1 or > MaxPageSize)
                throw new ApiException(StatusCodes.Status400BadRequest, $"pageSize must be between 1 and {MaxPageSize}.");
            var actualPage = page ?? 1;
            var actualPageSize = pageSize ?? DefaultPageSize;
            if ((long)(actualPage - 1) * actualPageSize > int.MaxValue)
                throw new ApiException(StatusCodes.Status400BadRequest, "The requested page is too large.");

            var query = db.Expenses.AsNoTracking().Where(row => row.OwnerId == ownerId && row.DeletedAt == null);
            if (dateFrom is DateOnly start) query = query.Where(row => row.ExpenseDate >= start);
            if (dateTo is DateOnly end) query = query.Where(row => row.ExpenseDate <= end);
            if (buildingId is Guid building) query = query.Where(row => row.BuildingId == building);
            if (roomId is Guid room) query = query.Where(row => row.RoomId == room);
            if (!string.IsNullOrWhiteSpace(category))
            {
                var normalized = ExpenseSupport.NormalizeCategory(category);
                query = query.Where(row => row.Category == normalized);
            }

            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var summary = await query.GroupBy(_ => 1)
                .Select(group => new { Total = group.Sum(row => row.Amount), Count = group.LongCount() })
                .SingleOrDefaultAsync(ct);
            var byCategory = await query.GroupBy(row => row.Category)
                .Select(group => new ExpenseCategoryTotal(group.Key, group.Sum(row => row.Amount), group.LongCount()))
                .ToListAsync(ct);
            var rows = await query.OrderByDescending(row => row.ExpenseDate).ThenByDescending(row => row.CreatedAt).ThenBy(row => row.Id)
                .Skip((actualPage - 1) * actualPageSize).Take(actualPageSize).ToListAsync(ct);
            var items = await ToDtosAsync(db, ownerId, rows, ct);
            await snapshot.CommitAsync(ct);

            var totals = new ExpenseListTotals(summary?.Total ?? 0m, summary?.Count ?? 0,
                byCategory.OrderByDescending(row => row.Amount).ThenBy(row => row.Category, StringComparer.Ordinal).ToList());
            return Results.Ok(new ExpenseListDto(items, actualPage, actualPageSize, summary?.Count ?? 0, totals));
        });

        routes.MapGet("/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var expense = await db.Expenses.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId && row.DeletedAt == null, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy khoản chi.");
            return Results.Ok((await ToDtosAsync(db, ownerId, [expense], ct))[0]);
        });

        routes.MapPost("", async (ExpenseRequest request, HttpContext context, AppDbContext db, TimeProvider clock,
            [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null,
            CancellationToken ct = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var key = ExpenseSupport.NormalizeIdempotencyKey(context.Request.Headers["Idempotency-Key"].FirstOrDefault());
            var category = ExpenseSupport.NormalizeCategory(request.Category);
            var description = ExpenseSupport.NormalizeDescription(request.Description);
            var fingerprint = RequestFingerprint.Fingerprint(new
            {
                request.ExpenseDate, Category = category, request.Amount, Description = description,
                request.BuildingId, request.RoomId, request.UtilityPeriod, request.UtilityKwh
            });

            if (key is not null)
            {
                var replay = await FindReplayAsync(db, ownerId, key, fingerprint, ct);
                if (replay is not null) return Results.Ok((await ToDtosAsync(db, ownerId, [replay], ct))[0]);
            }

            var now = clock.GetUtcNow();
            ExpenseSupport.ValidateAmount(request.Amount);
            ValidateExpenseDate(request.ExpenseDate, now);
            var (buildingId, roomId) = await ExpenseSupport.ResolveLocationAsync(db, ownerId, request.BuildingId, request.RoomId, null, null, ct);
            var (utilityPeriod, utilityKwh) = await ValidateUtilityAsync(db, ownerId, category, buildingId, request.UtilityPeriod,
                request.UtilityKwh, null, null, now, ct);

            var expense = new Expense
            {
                OwnerId = ownerId, BuildingId = buildingId, RoomId = roomId, Category = category, Amount = request.Amount,
                ExpenseDate = request.ExpenseDate, Description = description, UtilityPeriod = utilityPeriod, UtilityKwh = utilityKwh,
                IdempotencyKey = key, RequestFingerprint = fingerprint, CreatedAt = now, UpdatedAt = now
            };
            db.Expenses.Add(expense);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (key is not null && ExpenseSupport.IsUniqueViolation(exception))
            {
                // A concurrent request with the same key won the insert; answer with its record or reject a different body.
                db.ChangeTracker.Clear();
                var winner = await FindReplayAsync(db, ownerId, key, fingerprint, ct)
                    ?? throw new ApiException(StatusCodes.Status409Conflict, "Another request changed this record. Refresh and retry.");
                return Results.Ok((await ToDtosAsync(db, ownerId, [winner], ct))[0]);
            }
            await RecordActivityAsync(milestoneTracker, ownerId, "ExpenseCreate", ct);
            return Results.Created($"/api/expenses/{expense.Id}", (await ToDtosAsync(db, ownerId, [expense], ct))[0]);
        });

        routes.MapPut("/{id:guid}", async (Guid id, ExpenseRequest request, HttpContext context, AppDbContext db, TimeProvider clock,
            [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null,
            CancellationToken ct = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (request.ExpectedVersion is not long expectedVersion)
                throw new ApiException(StatusCodes.Status400BadRequest, "expectedVersion is required.");
            var expense = await db.Expenses.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId && row.DeletedAt == null, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy khoản chi.");
            if (expense.Version != expectedVersion)
                throw new ApiException(StatusCodes.Status409Conflict, "Khoản chi đã thay đổi. Tải lại rồi thử lại.");

            var now = clock.GetUtcNow();
            var category = ExpenseSupport.NormalizeCategory(request.Category);
            var description = ExpenseSupport.NormalizeDescription(request.Description);
            ExpenseSupport.ValidateAmount(request.Amount);
            ValidateExpenseDate(request.ExpenseDate, now);
            var (buildingId, roomId) = await ExpenseSupport.ResolveLocationAsync(db, ownerId, request.BuildingId, request.RoomId,
                expense.BuildingId, expense.RoomId, ct);
            var (utilityPeriod, utilityKwh) = await ValidateUtilityAsync(db, ownerId, category, buildingId, request.UtilityPeriod,
                request.UtilityKwh, expense.UtilityPeriod, expense.UtilityKwh, now, ct);

            expense.Category = category;
            expense.Amount = request.Amount;
            expense.ExpenseDate = request.ExpenseDate;
            expense.Description = description;
            expense.BuildingId = buildingId;
            expense.RoomId = roomId;
            expense.UtilityPeriod = utilityPeriod;
            expense.UtilityKwh = utilityKwh;
            expense.Version++;
            expense.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await RecordActivityAsync(milestoneTracker, ownerId, "ExpenseUpdate", ct);
            return Results.Ok((await ToDtosAsync(db, ownerId, [expense], ct))[0]);
        });

        routes.MapDelete("/{id:guid}", async (Guid id, long? expectedVersion, HttpContext context, AppDbContext db, TimeProvider clock,
            [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null,
            CancellationToken ct = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (expectedVersion is not long version)
                throw new ApiException(StatusCodes.Status400BadRequest, "expectedVersion is required.");
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var expense = await db.Expenses.SingleOrDefaultAsync(row => row.Id == id && row.OwnerId == ownerId && row.DeletedAt == null, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy khoản chi.");
            if (expense.Version != version)
                throw new ApiException(StatusCodes.Status409Conflict, "Khoản chi đã thay đổi. Tải lại rồi thử lại.");
            var now = clock.GetUtcNow();
            expense.DeletedAt = now;
            expense.UpdatedAt = now;
            expense.Version++;
            // Attachments of a deleted expense are unreachable; drop them so they stop counting against the quota.
            await db.ExpenseAttachments.Where(row => row.ExpenseId == expense.Id && row.OwnerId == ownerId).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await RecordActivityAsync(milestoneTracker, ownerId, "ExpenseDelete", ct);
            return Results.Ok(new { deleted = true, id });
        });
    }

    private static async Task<Expense?> FindReplayAsync(AppDbContext db, Guid ownerId, string key, string fingerprint, CancellationToken ct)
    {
        var prior = await db.Expenses.AsNoTracking().SingleOrDefaultAsync(row => row.OwnerId == ownerId && row.IdempotencyKey == key, ct);
        if (prior is null) return null;
        if (!string.Equals(prior.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new ApiException(StatusCodes.Status409Conflict, "This idempotency key was already used for a different expense.");
        if (prior.DeletedAt is not null)
            throw new ApiException(StatusCodes.Status409Conflict, "Khoản chi của yêu cầu này đã bị xóa.");
        return prior;
    }

    private static void ValidateExpenseDate(DateOnly date, DateTimeOffset now)
    {
        if (date < new DateOnly(2000, 1, 1))
            throw new ApiException(StatusCodes.Status400BadRequest, "Ngày chi không hợp lệ.");
        if (date > ApiSupport.VietnamDate(now))
            throw new ApiException(StatusCodes.Status400BadRequest, "Ngày chi không được ở tương lai.");
    }

    private static async Task<(string? Period, decimal? Kwh)> ValidateUtilityAsync(AppDbContext db, Guid ownerId, string category,
        Guid? buildingId, string? requestedPeriod, decimal? requestedKwh, string? currentPeriod, decimal? currentKwh,
        DateTimeOffset now, CancellationToken ct)
    {
        var hasPeriod = !string.IsNullOrWhiteSpace(requestedPeriod);
        if (!hasPeriod && requestedKwh is null) return (null, null);
        if (!hasPeriod || requestedKwh is null)
            throw new ApiException(StatusCodes.Status400BadRequest, "Cần nhập cả kỳ đối chiếu và số kWh EVN.");
        if (category != ExpenseCategories.Electricity)
            throw new ApiException(StatusCodes.Status400BadRequest, "Chỉ khoản chi điện tổng mới có số kWh EVN.");
        if (buildingId is null)
            throw new ApiException(StatusCodes.Status400BadRequest, "Chọn dãy trọ để đối chiếu điện EVN.");
        var period = ApiSupport.NormalizePeriod(requestedPeriod);
        var kwh = requestedKwh.Value;
        if (kwh < 0 || kwh > ExpenseLimits.MaxKwh || decimal.Round(kwh, 2) != kwh)
            throw new ApiException(StatusCodes.Status400BadRequest, "Số kWh EVN phải từ 0 đến 10.000.000, tối đa 2 chữ số thập phân.");
        if (period != currentPeriod || kwh != currentKwh)
            await ExpenseSupport.EnsureProAsync(db, ownerId, now, ct, "Đối chiếu điện EVN dành cho gói Pro. Vui lòng nâng cấp để sử dụng.");
        return (period, kwh);
    }

    private static async Task<List<ExpenseDto>> ToDtosAsync(AppDbContext db, Guid ownerId, IReadOnlyList<Expense> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var (buildings, rooms) = await ExpenseSupport.LoadNamesAsync(db, ownerId, rows.Select(row => row.BuildingId), rows.Select(row => row.RoomId), ct);
        var ids = rows.Select(row => row.Id).ToList();
        var counts = await db.ExpenseAttachments.AsNoTracking().Where(row => row.OwnerId == ownerId && ids.Contains(row.ExpenseId))
            .GroupBy(row => row.ExpenseId).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct);
        return rows.Select(row => new ExpenseDto(row.Id, row.ExpenseDate, row.Category, row.Amount, row.Description,
            row.BuildingId, row.BuildingId is Guid b ? buildings.GetValueOrDefault(b) : null,
            row.RoomId, row.RoomId is Guid r ? rooms.GetValueOrDefault(r) : null,
            row.UtilityPeriod, row.UtilityKwh, row.RecurringExpenseId, row.RecurrencePeriod,
            counts.GetValueOrDefault(row.Id), row.Version, row.CreatedAt, row.UpdatedAt)).ToList();
    }

    private static async Task RecordActivityAsync(RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? tracker, Guid ownerId,
        string activity, CancellationToken ct)
    {
        if (tracker is null) return;
        try { await tracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, activity, ct); }
        catch { /* Telemetry failure must not block product */ }
    }
}
