namespace RoomRental.Api.Domain;

public enum TenantMeterSubmissionStatus { Pending, Approved, Rejected }

public sealed class TenantMeterSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid RoomId { get; set; }
    public Guid OccupancyId { get; set; }
    public required string Period { get; set; }
    public decimal PreviousReading { get; set; }
    public decimal SubmittedReading { get; set; }
    public decimal? ApprovedReading { get; set; }
    public string? TenantNote { get; set; }
    public string? RejectReason { get; set; }
    public TenantMeterSubmissionStatus Status { get; set; } = TenantMeterSubmissionStatus.Pending;
    public required string PhotoFileName { get; set; }
    public required string PhotoContentType { get; set; }
    public long PhotoSizeBytes { get; set; }
    public required string PhotoSha256 { get; set; }
    public required byte[] PhotoContent { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReadingId { get; set; }
    public Guid? InvoiceId { get; set; }
}

public sealed class TenantIdentityDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid RoomId { get; set; }
    public Guid OccupancyId { get; set; }
    public string TargetKey { get; set; } = "tenant";
    public string PhotoFileName { get; set; } = "";
    public string PhotoContentType { get; set; } = "";
    public long PhotoSizeBytes { get; set; }
    public string PhotoSha256 { get; set; } = "";
    public byte[] PhotoContent { get; set; } = [];
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
