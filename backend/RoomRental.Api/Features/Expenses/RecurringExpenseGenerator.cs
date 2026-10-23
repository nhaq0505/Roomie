using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed record RecurringGenerationResult(int Templates, int Created, int Failed);

/// <summary>
/// Materializes monthly expenses from templates. Every insert is guarded by the unique
/// (RecurringExpenseId, RecurrencePeriod) index with ON CONFLICT DO NOTHING, so restarts, overlapping runs or a
/// second process can never duplicate an expense, and a soft-deleted generated expense is never recreated.
/// </summary>
public sealed class RecurringExpenseGenerator(AppDbContext db, TimeProvider clock, ILogger<RecurringExpenseGenerator> logger)
{
    public const string SkipProRequired = "ProRequired";
    public const string SkipLocationArchived = "LocationArchived";
    public const string SkipEnded = "Ended";
    public const string SkipPaused = "Paused";
    public const string SkipNotStarted = "NotStarted";

    public static IReadOnlyList<(string Period, DateOnly Date)> DuePeriods(RecurringExpense template, DateOnly today)
    {
        var current = ExpenseSupport.CurrentPeriod(today);
        var backfillStart = ExpenseSupport.AddMonths(current, -ExpenseLimits.RecurringBackfillMonths);
        var period = string.CompareOrdinal(template.StartPeriod, backfillStart) > 0 ? template.StartPeriod : backfillStart;
        var result = new List<(string, DateOnly)>();
        while (string.CompareOrdinal(period, current) <= 0)
        {
            if (template.EndPeriod is not null && string.CompareOrdinal(period, template.EndPeriod) > 0) break;
            var date = new DateOnly(int.Parse(period.AsSpan(0, 4)), int.Parse(period.AsSpan(5, 2)), template.DayOfMonth);
            if (date <= today) result.Add((period, date));
            period = ExpenseSupport.AddMonths(period, 1);
        }
        return result;
    }

    public static string? SkipReason(RecurringExpense template, bool isPro, bool locationArchived, DateOnly today)
    {
        if (template.PausedAt is not null) return SkipPaused;
        if (!isPro) return SkipProRequired;
        if (locationArchived) return SkipLocationArchived;
        var current = ExpenseSupport.CurrentPeriod(today);
        if (template.EndPeriod is not null && string.CompareOrdinal(template.EndPeriod, current) < 0) return SkipEnded;
        if (string.CompareOrdinal(template.StartPeriod, current) > 0) return SkipNotStarted;
        return null;
    }

    /// <summary>Inserts the missing due expenses for one template. Callers must have checked plan and location state.</summary>
    public async Task<int> GenerateForTemplateAsync(RecurringExpense template, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var created = 0;
        foreach (var (period, date) in DuePeriods(template, today))
        {
            var id = Guid.NewGuid();
            created += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Expenses" ("Id", "OwnerId", "BuildingId", "RoomId", "Category", "Amount", "ExpenseDate", "Description",
                    "RecurringExpenseId", "RecurrencePeriod", "Version", "CreatedAt", "UpdatedAt")
                VALUES ({id}, {template.OwnerId}, {template.BuildingId}, {template.RoomId}, {template.Category}, {template.Amount},
                    {date}, {template.Description}, {template.Id}, {period}, 1, {now}, {now})
                ON CONFLICT ("RecurringExpenseId", "RecurrencePeriod") WHERE "RecurringExpenseId" IS NOT NULL DO NOTHING
                """, ct);
        }
        return created;
    }

    public async Task<RecurringGenerationResult> GenerateAllAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var current = ExpenseSupport.CurrentPeriod(today);
        var earliest = ExpenseSupport.AddMonths(current, -ExpenseLimits.RecurringBackfillMonths);
        var templates = await (from template in db.RecurringExpenses.AsNoTracking()
                               join owner in db.Users.AsNoTracking() on template.OwnerId equals owner.Id
                               where template.DeletedAt == null && template.PausedAt == null &&
                                     owner.Status == OwnerStatus.Approved && owner.SubscriptionTier == SubscriptionTier.Pro &&
                                     (owner.SubscriptionExpiresAt == null || owner.SubscriptionExpiresAt > now) &&
                                     string.Compare(template.StartPeriod, current) <= 0 &&
                                     (template.EndPeriod == null || string.Compare(template.EndPeriod, earliest) >= 0) &&
                                     (template.BuildingId == null || db.Buildings.Any(building => building.Id == template.BuildingId &&
                                         building.OwnerId == template.OwnerId && building.ArchivedAt == null)) &&
                                     (template.RoomId == null || db.Rooms.Any(room => room.Id == template.RoomId && room.ArchivedAt == null))
                               orderby template.OwnerId, template.Id
                               select template).ToListAsync(ct);
        int created = 0, failed = 0;
        foreach (var template in templates)
        {
            try
            {
                created += await GenerateForTemplateAsync(template, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // One broken template must not block other owners; it is retried on the next run.
                failed++;
                logger.LogError(exception, "Recurring expense generation failed for template {TemplateId}.", template.Id);
            }
        }
        return new RecurringGenerationResult(templates.Count, created, failed);
    }
}

public sealed class RecurringExpenseWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration,
    ILogger<RecurringExpenseWorker> logger, TimeProvider clock) : BackgroundService
{
    private const string WorkerName = "recurring-expenses";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("RecurringExpenses:IntervalMinutes", 60), 5, 1440));
        try { await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync(WorkerName, isEnabled: true, status: "Running", errorMessage: null, stoppingToken);
                var result = await scope.ServiceProvider.GetRequiredService<RecurringExpenseGenerator>().GenerateAllAsync(stoppingToken);
                if (result.Created > 0) logger.LogInformation("Generated {Count} recurring expenses from {Templates} templates.", result.Created, result.Templates);
                if (tracker != null)
                    await tracker.RecordWorkerHeartbeatAsync(WorkerName, isEnabled: true, status: result.Failed == 0 ? "Healthy" : "Failed",
                        errorMessage: result.Failed == 0 ? null : $"{result.Failed} template(s) failed", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Recurring expense run failed.");
                try
                {
                    using var errorScope = scopeFactory.CreateScope();
                    var tracker = errorScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                    if (tracker != null) await tracker.RecordWorkerHeartbeatAsync(WorkerName, isEnabled: true, status: "Failed", errorMessage: exception.Message, stoppingToken);
                }
                catch { /* Telemetry failure must not stop the worker */ }
            }

            try { await Task.Delay(interval, clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
