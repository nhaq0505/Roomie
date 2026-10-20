using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected AppDbContext(DbContextOptions options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<LoginThrottle> LoginThrottles => Set<LoginThrottle>();
    public DbSet<RegistrationVerification> RegistrationVerifications => Set<RegistrationVerification>();
    public DbSet<PasswordRecovery> PasswordRecoveries => Set<PasswordRecovery>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<MonthlyReading> Readings => Set<MonthlyReading>();
    public DbSet<DepositSettlement> DepositSettlements => Set<DepositSettlement>();
    public DbSet<MoveoutCompletion> MoveoutCompletions => Set<MoveoutCompletion>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceRevision> InvoiceRevisions => Set<InvoiceRevision>();
    public DbSet<PaymentReceipt> Receipts => Set<PaymentReceipt>();
    public DbSet<OwnerPaymentSettings> PaymentSettings => Set<OwnerPaymentSettings>();
    public DbSet<PaymentWebhookRegistration> PaymentWebhookRegistrations => Set<PaymentWebhookRegistration>();
    public DbSet<PayOsAttempt> PayOsAttempts => Set<PayOsAttempt>();
    public DbSet<ProcessedWebhook> ProcessedWebhooks => Set<ProcessedWebhook>();

    public DbSet<OwnerZaloSettings> ZaloSettings => Set<OwnerZaloSettings>();
    public DbSet<ZaloReminder> ZaloReminders => Set<ZaloReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OwnerZaloSettings>(e =>
        {
            e.HasKey(x => x.OwnerId);
            e.Property(x => x.BankBin).HasMaxLength(6);
            e.Property(x => x.BankAccount).HasMaxLength(32);
            e.Property(x => x.BankAccountName).HasMaxLength(100);
            e.Property(x => x.LastScheduledPeriod).HasMaxLength(7);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ZaloReminder>(e =>
        {
            e.HasKey(x => x.InvoiceId);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(200);
            e.HasIndex(x => new { x.OwnerId, x.Status, x.QueuedAtMs });
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PaymentWebhookRegistration>().HasKey(x => x.OwnerId);
        if (Database.IsSqlite())
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties())
                         .Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Phone).HasMaxLength(60);
            entity.Property(x => x.PasswordHash).HasMaxLength(1024).IsConcurrencyToken();
            entity.Property(x => x.PasswordResetHash).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.StatusReason).HasMaxLength(1000);
            entity.Property(x => x.SubscriptionTier).HasConversion<string>().HasMaxLength(24);
        });

        modelBuilder.Entity<LoginThrottle>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(64);
            entity.Property(x => x.Attempts).IsConcurrencyToken();
            entity.HasIndex(x => x.WindowStartedAt);
        });
        modelBuilder.Entity<AppUser>().Property(x => x.MfaLastStep).IsConcurrencyToken();
        modelBuilder.Entity<AppUser>().Property(x => x.MfaRecoveryHashes).IsConcurrencyToken();
        modelBuilder.Entity<RegistrationVerification>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<AuthSession>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<PasswordRecovery>(entity =>
        {
            entity.HasKey(x => x.Email);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.HasIndex(x => x.ChallengeHash).IsUnique();
            entity.HasIndex(x => x.ResetHash).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.Property(x => x.ChallengeHash).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(x => x.CodeHash).HasMaxLength(64);
            entity.Property(x => x.ResetHash).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(x => x.SendCount).IsConcurrencyToken();
            entity.Property(x => x.FailedAttempts).IsConcurrencyToken();
            entity.Property(x => x.ConsumedAt).IsConcurrencyToken();
        });
        modelBuilder.Entity<RegistrationVerification>(entity =>
        {
            entity.HasKey(x => x.Email);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.HasIndex(x => x.ChallengeHash).IsUnique();
            entity.Property(x => x.ChallengeHash).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(x => x.SendCount).IsConcurrencyToken();
            entity.Property(x => x.FailedAttempts).IsConcurrencyToken();
            entity.Property(x => x.ConsumedAt).IsConcurrencyToken();
        });

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        modelBuilder.Entity<Building>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OwnerId);
            entity.HasIndex(x => x.ArchivedAt);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Address).HasMaxLength(500);
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BuildingId);
            entity.HasIndex(x => x.ArchivedAt);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Rent).HasPrecision(18, 2);
            entity.Property(x => x.ElectricityRate).HasPrecision(18, 2);
            entity.Property(x => x.WaterFee).HasPrecision(18, 2);
            entity.Property(x => x.ServiceFee).HasPrecision(18, 2);
            entity.Property(x => x.InitialReading).HasPrecision(18, 3);
        });

        modelBuilder.Entity<Occupancy>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RoomId, x.IsActive });
            entity.HasIndex(x => x.TenantTokenHash).IsUnique();
            entity.Property(x => x.TenantName).HasMaxLength(160);
            entity.Property(x => x.Phone).HasMaxLength(60);
            entity.Property(x => x.TenantTokenHash).HasMaxLength(64);
            entity.Property(x => x.ProtectedTenantToken).HasMaxLength(2048);
        });

        modelBuilder.Entity<MonthlyReading>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OccupancyId, x.Period }).IsUnique();
            entity.HasIndex(x => new { x.RoomId, x.Period });
            entity.Property(x => x.Period).HasMaxLength(7);
            entity.Property(x => x.PreviousReading).HasPrecision(18, 3);
            entity.Property(x => x.CurrentReading).HasPrecision(18, 3);
            entity.Property(x => x.Consumption).HasPrecision(18, 3);
            entity.Property(x => x.MonthlyRentRate).HasPrecision(18, 2);
            entity.Property(x => x.ElectricityRate).HasPrecision(18, 2);
            entity.Property(x => x.WaterRate).HasPrecision(18, 2);
            entity.Property(x => x.ServiceRate).HasPrecision(18, 2);
        });

        modelBuilder.Entity<DepositSettlement>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InvoiceId, x.IdempotencyKey }).IsUnique();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<MoveoutCompletion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.OccupancyId).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.CompletedAt });
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64);
            entity.Property(x => x.HeldBeforeSettlement).HasPrecision(18, 2);
            entity.Property(x => x.RefundAmount).HasPrecision(18, 2);
            entity.Property(x => x.RetainedAmount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OccupancyId, x.Period }).IsUnique().HasFilter("\"Kind\" = 'Monthly'");
            entity.HasIndex(x => x.OccupancyId).IsUnique().HasDatabaseName("IX_Invoices_OccupancyId_Deposit")
                .HasFilter("\"Kind\" = 'Deposit'");
            entity.HasMany(x => x.DepositSettlements).WithOne().HasForeignKey(x => x.InvoiceId);
            entity.HasIndex(x => new { x.OwnerId, x.Period });
            entity.HasIndex(x => x.OccupancyId);
            entity.HasIndex(x => x.BuildingId);
            entity.Property(x => x.RoomName).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.OccupancyId, "FinalMeterInvoice").IsUnique().HasDatabaseName("IX_Invoices_OccupancyId_FinalMeter").HasFilter("\"IsFinalMeterReading\" = TRUE");
            entity.Property(x => x.BuildingName).HasMaxLength(160);
            entity.Property(x => x.Period).HasMaxLength(7);
            entity.Property(x => x.TenantName).HasMaxLength(160);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.LifecycleState).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(InvoiceLifecycleState.Issued).HasSentinel(InvoiceLifecycleState.Issued);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.PreviousReading).HasPrecision(18, 3);
            entity.Property(x => x.CurrentReading).HasPrecision(18, 3);
            entity.Property(x => x.Consumption).HasPrecision(18, 3);
            entity.Property(x => x.Rent).HasPrecision(18, 2);
            entity.Property(x => x.ElectricityAmount).HasPrecision(18, 2);
            entity.Property(x => x.WaterAmount).HasPrecision(18, 2);
            entity.Property(x => x.ServiceAmount).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Paid).HasPrecision(18, 2);
            entity.Property(x => x.MonthlyRentRate).HasPrecision(18, 2);
            entity.Property(x => x.ElectricityRate).HasPrecision(18, 2);
            entity.Property(x => x.WaterRate).HasPrecision(18, 2);
            entity.Property(x => x.ServiceRate).HasPrecision(18, 2);
        });

        modelBuilder.Entity<InvoiceRevision>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InvoiceId, x.RequestId }).IsUnique();
            entity.HasIndex(x => new { x.InvoiceId, x.Revision }).IsUnique();
            entity.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.ActorName).HasMaxLength(160);
            entity.Property(x => x.BeforeJson).HasColumnType("text");
            entity.Property(x => x.AfterJson).HasColumnType("text");
        });

        modelBuilder.Entity<PaymentReceipt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InvoiceId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.ExternalReference }).IsUnique();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.Property(x => x.Source).HasMaxLength(30);
            entity.Property(x => x.ExternalReference).HasMaxLength(160);
        });

        modelBuilder.Entity<OwnerPaymentSettings>(entity =>
        {
            entity.HasKey(x => x.OwnerId);
            entity.Property(x => x.ClientId).HasMaxLength(160);
            entity.Property(x => x.ProtectedApiKey).HasMaxLength(4096);
            entity.Property(x => x.ProtectedChecksumKey).HasMaxLength(4096);
        });

        modelBuilder.Entity<PayOsAttempt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderCode).IsUnique();
            entity.HasIndex(x => new { x.InvoiceId, x.Status });
            entity.HasIndex(x => x.InvoiceId).IsUnique().HasDatabaseName("IX_PayOsAttempts_InvoiceId_Pending")
                .HasFilter("\"Status\" = 'Pending'");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            entity.Property(x => x.Kind).HasMaxLength(40);
            entity.Property(x => x.CheckoutUrl).HasMaxLength(2048);
            entity.Property(x => x.PaymentLinkId).HasMaxLength(160);
            entity.Property(x => x.QrCode).HasMaxLength(4096);
            entity.Property(x => x.AccountName).HasMaxLength(200);
            entity.Property(x => x.AccountNumber).HasMaxLength(100);
            entity.Property(x => x.BankBin).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.ProviderStatus).HasMaxLength(40);
            entity.Property(x => x.ProviderReference).HasMaxLength(160);
        });

        modelBuilder.Entity<ProcessedWebhook>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OwnerId, x.Reference }).IsUnique();
            entity.Property(x => x.Reference).HasMaxLength(160);
        });
    }
}
