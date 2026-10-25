namespace RoomRental.Api.Domain;

public sealed class OwnerWhatsAppSettings
{
    public Guid OwnerId { get; set; }
    public bool ScheduleEnabled { get; set; }
    public int DayOfMonth { get; set; } = 5;
    public int Hour { get; set; } = 8;
    public int Minute { get; set; }
    public string BankBin { get; set; } = "";
    public string BankAccount { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string LastScheduledPeriod { get; set; } = "";
    public long DispatchLeaseUntilMs { get; set; }
    public long NextSendAtMs { get; set; }
}

// One durable queue slot per invoice: concurrent clicks and monthly runs cannot duplicate work.
public sealed class WhatsAppReminder
{
    public Guid InvoiceId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid AttemptId { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "Pending";
    public string? Detail { get; set; }
    public long QueuedAtMs { get; set; }
    public long LastAttemptAtMs { get; set; }
    public long? SentAtMs { get; set; }
}
