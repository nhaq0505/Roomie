namespace RoomRental.Api.Domain;

/// <summary>
/// Owner operating expense. Amounts are whole VND; ExpenseDate is the Vietnam business date.
/// Soft-deleted rows keep their recurrence slot so a deleted generated expense is never regenerated.
/// </summary>
public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? RoomId { get; set; }
    public required string Category { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string? Description { get; set; }
    public string? UtilityPeriod { get; set; }
    public decimal? UtilityKwh { get; set; }
    public Guid? RecurringExpenseId { get; set; }
    public string? RecurrencePeriod { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestFingerprint { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Monthly expense template materialized by <c>RecurringExpenseGenerator</c>.</summary>
public sealed class RecurringExpense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? RoomId { get; set; }
    public required string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public int DayOfMonth { get; set; }
    public required string StartPeriod { get; set; }
    public string? EndPeriod { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestFingerprint { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Receipt/voucher file kept in PostgreSQL so the existing pg_dump backup covers it.</summary>
public sealed class ExpenseAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid ExpenseId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ExpenseCategories
{
    public const string Electricity = "Electricity";

    public static readonly IReadOnlyList<string> Ordered =
        ["Electricity", "Water", "Internet", "Repair", "Cleaning", "Salary", "LeaseRent", "Tax", "Other"];

    public static readonly IReadOnlySet<string> All = new HashSet<string>(Ordered, StringComparer.Ordinal);
}

public static class ExpenseLimits
{
    public const decimal MaxAmount = 10_000_000_000m;
    public const decimal MaxKwh = 10_000_000m;
    public const int MaxDescriptionLength = 500;
    public const long MaxAttachmentBytes = 2 * 1024 * 1024;
    public const int MaxAttachmentsPerExpense = 5;
    public const long MaxAttachmentBytesPerOwner = 200L * 1024 * 1024;
    public const int RecurringBackfillMonths = 2;
    public const int MaxReportMonths = 24;
}

public sealed record ExpenseRequest(DateOnly ExpenseDate, string? Category, decimal Amount, string? Description,
    Guid? BuildingId, Guid? RoomId, string? UtilityPeriod = null, decimal? UtilityKwh = null, long? ExpectedVersion = null);

public sealed record ExpenseDto(Guid Id, DateOnly ExpenseDate, string Category, decimal Amount, string? Description,
    Guid? BuildingId, string? BuildingName, Guid? RoomId, string? RoomName, string? UtilityPeriod, decimal? UtilityKwh,
    Guid? RecurringExpenseId, string? RecurrencePeriod, int AttachmentCount, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExpenseCategoryTotal(string Category, decimal Amount, long Count);

public sealed record ExpenseListTotals(decimal Total, long Count, IReadOnlyList<ExpenseCategoryTotal> ByCategory);

public sealed record ExpenseListDto(IReadOnlyList<ExpenseDto> Items, int Page, int PageSize, long TotalCount, ExpenseListTotals Totals);

public sealed record RecurringExpenseRequest(string? Category, decimal Amount, string? Description, Guid? BuildingId,
    Guid? RoomId, int DayOfMonth, string? StartPeriod, string? EndPeriod, long? ExpectedVersion = null);

public sealed record RecurringExpenseStateRequest(long? ExpectedVersion);

public sealed record RecurringExpenseDto(Guid Id, string Category, decimal Amount, string? Description,
    Guid? BuildingId, string? BuildingName, Guid? RoomId, string? RoomName, int DayOfMonth, string StartPeriod,
    string? EndPeriod, bool IsPaused, string? SkipReason, string? LastGeneratedPeriod, long Version,
    DateTimeOffset CreatedAt);

public sealed record ExpenseAttachmentDto(Guid Id, Guid ExpenseId, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record ExpenseAttachmentUsageDto(long UsedBytes, long LimitBytes, long MaxFileBytes, int MaxFilesPerExpense);

public sealed record ProfitLossMonth(string Month, decimal Income, decimal Expenses, decimal Net);

public sealed record ProfitLossTotals(decimal Income, decimal Expenses, decimal Net);

public sealed record ProfitLossReportDto(DateOnly DateFrom, DateOnly DateTo, Guid? BuildingId, ProfitLossTotals Totals,
    IReadOnlyList<ProfitLossMonth> Months, IReadOnlyList<ExpenseCategoryTotal> ExpensesByCategory, decimal SharedExpensesExcluded);

public sealed record ElectricityReconciliationRow(Guid BuildingId, string BuildingName, string Period,
    decimal? EvnKwh, decimal EvnAmount, int EvnBillCount, decimal BilledKwh, decimal BilledAmount,
    decimal? LossKwh, decimal? LossPercent, string? LossNullReason, decimal Margin, int DraftInvoiceCount);

public sealed record ElectricityReconciliationDto(string PeriodFrom, string PeriodTo, Guid? BuildingId,
    IReadOnlyList<ElectricityReconciliationRow> Rows);
