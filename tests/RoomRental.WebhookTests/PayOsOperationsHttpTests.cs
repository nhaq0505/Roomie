using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class PayOsOperationsHttpTests
{
    private const string ChecksumKeyA = "checksum-key-owner-a-32-chars-long";
    private const string ChecksumKeyB = "checksum-key-owner-b-32-chars-long";

    public static async Task RunAsync()
    {
        await WebhookHttpEndpointAsync();
        await ReconciliationBatchSweepAsync();
        await DatabaseIntegrityVerificationAsync();
    }

    private static async Task WebhookHttpEndpointAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var handler = new FakePayOsReconcileHandler();
        await using var app = CreateTestApp(fixture.ConnectionString, handler);
        await app.StartAsync();
        using var client = app.GetTestClient();

        var ownerAId = Guid.NewGuid();
        var ownerBId = Guid.NewGuid();
        var invoiceAId = Guid.NewGuid();
        const long orderCodeA = 9_000_000_001;
        const int amountA = 500_000;

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var protector = protection.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");

            db.Users.AddRange(
                new AppUser { Id = ownerAId, Name = "Owner A", Email = "ownerA@payos.test", PasswordHash = "unused", Role = UserRole.Owner, SubscriptionTier = SubscriptionTier.Free },
                new AppUser { Id = ownerBId, Name = "Owner B", Email = "ownerB@payos.test", PasswordHash = "unused", Role = UserRole.Owner, SubscriptionTier = SubscriptionTier.Free }
            );

            db.PaymentSettings.AddRange(
                new OwnerPaymentSettings
                {
                    OwnerId = ownerAId, ClientId = "client-a",
                    ProtectedApiKey = protector.Protect("api-key-a"),
                    ProtectedChecksumKey = protector.Protect(ChecksumKeyA),
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                new OwnerPaymentSettings
                {
                    OwnerId = ownerBId, ClientId = "client-b",
                    ProtectedApiKey = protector.Protect("api-key-b"),
                    ProtectedChecksumKey = protector.Protect(ChecksumKeyB),
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            );

            db.Invoices.Add(new Invoice
            {
                Id = invoiceAId, OwnerId = ownerAId, RoomId = Guid.NewGuid(), OccupancyId = Guid.NewGuid(),
                RoomName = "Room 101", BuildingName = "Building 1", TenantName = "Tenant 1",
                Period = "2026-10", Kind = "Monthly", LifecycleState = InvoiceLifecycleState.Issued,
                Total = amountA, Paid = 0, Status = InvoiceStatus.Unpaid, Version = 1,
                DueDate = new DateOnly(2026, 10, 31)
            });

            db.PayOsAttempts.Add(new PayOsAttempt
            {
                OwnerId = ownerAId, InvoiceId = invoiceAId, OrderCode = orderCodeA, Amount = amountA,
                CheckoutUrl = "https://pay.payos.vn/web/test-link-a", Status = PaymentAttemptStatus.Pending
            });

            await db.SaveChangesAsync();
        }

        // 1. Valid signed webhook HTTP request to POST /api/payments/webhook/{ownerId}
        var validWebhookPayload = BuildWebhookPayload(orderCodeA, amountA, "ref-test-001", ChecksumKeyA);
        var webhookResp = await client.PostAsJsonAsync($"/api/payments/webhook/{ownerAId}", validWebhookPayload);
        Assert(webhookResp.StatusCode == HttpStatusCode.OK, "valid signed webhook returns 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.FindAsync(invoiceAId);
            Assert(invoice!.Paid == amountA && invoice.Status == InvoiceStatus.Paid,
                "invoice credited and marked Paid upon valid webhook");

            var attempt = await db.PayOsAttempts.SingleAsync(a => a.OrderCode == orderCodeA);
            Assert(attempt.Status == PaymentAttemptStatus.Paid && attempt.ProviderReference == "ref-test-001",
                "payment attempt marked Paid with provider reference");

            var receipts = await db.Receipts.Where(r => r.InvoiceId == invoiceAId).ToListAsync();
            Assert(receipts.Count == 1 && receipts[0].Source == "payOS" && receipts[0].Amount == amountA,
                "single payment receipt created with Source = 'payOS'");

            var processed = await db.ProcessedWebhooks.SingleAsync(w => w.Reference == "ref-test-001");
            Assert(processed.OwnerId == ownerAId && processed.OrderCode == orderCodeA,
                "processed webhook logged in database");
        }

        // 2. Webhook replay idempotency
        var replayResp = await client.PostAsJsonAsync($"/api/payments/webhook/{ownerAId}", validWebhookPayload);
        Assert(replayResp.StatusCode == HttpStatusCode.OK, "webhook replay is acknowledged with 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.Receipts.CountAsync(r => r.InvoiceId == invoiceAId) == 1,
                "webhook replay does not duplicate payment receipt");
            Assert((await db.Invoices.FindAsync(invoiceAId))!.Paid == amountA,
                "webhook replay does not double-credit invoice");
        }

        // 3. Invalid HMAC signature returns 401 Unauthorized
        var tamperedPayload = new
        {
            success = true,
            data = new Dictionary<string, string>
            {
                ["amount"] = amountA.ToString(),
                ["code"] = "00",
                ["orderCode"] = orderCodeA.ToString(),
                ["reference"] = "ref-tampered"
            },
            signature = new string('0', 64)
        };
        var tamperedResp = await client.PostAsJsonAsync($"/api/payments/webhook/{ownerAId}", tamperedPayload);
        Assert(tamperedResp.StatusCode == HttpStatusCode.Unauthorized,
            "webhook with invalid HMAC signature returns 401 Unauthorized");

        // 4. Owner scoping: webhook delivered to Owner B with Owner A's order code
        var crossOwnerPayload = BuildWebhookPayload(orderCodeA, amountA, "ref-cross-owner", ChecksumKeyB);
        var crossOwnerResp = await client.PostAsJsonAsync($"/api/payments/webhook/{ownerBId}", crossOwnerPayload);
        Assert(crossOwnerResp.StatusCode == HttpStatusCode.OK, "unmatched order safely acknowledged without crashing");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(!await db.Receipts.AnyAsync(r => r.ExternalReference == "ref-cross-owner"),
                "cross-owner webhook cannot credit another owner's records");
        }

        // Unconfigured callback owner returns 401 Unauthorized
        var unconfiguredOwnerResp = await client.PostAsJsonAsync($"/api/payments/webhook/{Guid.NewGuid()}", validWebhookPayload);
        Assert(unconfiguredOwnerResp.StatusCode == HttpStatusCode.Unauthorized,
            "webhook to unconfigured owner returns 401 Unauthorized");

        // 5. Valid signature but mismatched amount returns 409 Conflict
        const long orderCodeWrongAmount = 9_000_000_002;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PayOsAttempts.Add(new PayOsAttempt
            {
                OwnerId = ownerAId, InvoiceId = invoiceAId, OrderCode = orderCodeWrongAmount, Amount = 200_000,
                Status = PaymentAttemptStatus.Pending
            });
            await db.SaveChangesAsync();
        }

        var wrongAmountPayload = BuildWebhookPayload(orderCodeWrongAmount, 199_999, "ref-wrong-amount", ChecksumKeyA);
        var wrongAmountResp = await client.PostAsJsonAsync($"/api/payments/webhook/{ownerAId}", wrongAmountPayload);
        Assert(wrongAmountResp.StatusCode == HttpStatusCode.Conflict,
            "webhook with mismatched amount is rejected with 409 Conflict");
    }

    private static async Task ReconciliationBatchSweepAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var handler = new FakePayOsReconcileHandler();
        await using var app = CreateTestApp(fixture.ConnectionString, handler);
        await app.StartAsync();

        var ownerId = Guid.NewGuid();
        var invoicePaidId = Guid.NewGuid();
        var invoiceCancelledId = Guid.NewGuid();
        var invoicePendingId = Guid.NewGuid();

        const long orderPaid = 9_000_000_011;
        const long orderCancelled = 9_000_000_012;
        const long orderPending = 9_000_000_013;

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var protector = protection.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");

            db.Users.Add(new AppUser { Id = ownerId, Name = "Reconcile Owner", Email = "reconcile@test.invalid", PasswordHash = "unused", Role = UserRole.Owner });
            db.PaymentSettings.Add(new OwnerPaymentSettings
            {
                OwnerId = ownerId, ClientId = "reconcile-client",
                ProtectedApiKey = protector.Protect("reconcile-api"),
                ProtectedChecksumKey = protector.Protect(ChecksumKeyA),
                UpdatedAt = DateTimeOffset.UtcNow
            });

            // 1. Order to be confirmed PAID by provider
            db.Invoices.Add(new Invoice
            {
                Id = invoicePaidId, OwnerId = ownerId, RoomId = Guid.NewGuid(), OccupancyId = Guid.NewGuid(),
                RoomName = "R1", BuildingName = "B1", TenantName = "T1", Period = "2026-10", Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Issued, Total = 300_000, Paid = 0, Status = InvoiceStatus.Unpaid
            });
            db.PayOsAttempts.Add(new PayOsAttempt
            {
                OwnerId = ownerId, InvoiceId = invoicePaidId, OrderCode = orderPaid, Amount = 300_000,
                Status = PaymentAttemptStatus.Pending, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            handler.AddOrder(orderPaid, 300_000, "PAID", 300_000, ChecksumKeyA);

            // 2. Order to be confirmed CANCELLED by provider
            db.Invoices.Add(new Invoice
            {
                Id = invoiceCancelledId, OwnerId = ownerId, RoomId = Guid.NewGuid(), OccupancyId = Guid.NewGuid(),
                RoomName = "R2", BuildingName = "B1", TenantName = "T2", Period = "2026-10", Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Issued, Total = 150_000, Paid = 0, Status = InvoiceStatus.Unpaid
            });
            db.PayOsAttempts.Add(new PayOsAttempt
            {
                OwnerId = ownerId, InvoiceId = invoiceCancelledId, OrderCode = orderCancelled, Amount = 150_000,
                Status = PaymentAttemptStatus.Pending, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
            });
            handler.AddOrder(orderCancelled, 150_000, "CANCELLED", 0, ChecksumKeyA);

            // 3. Order remaining PENDING at provider
            db.Invoices.Add(new Invoice
            {
                Id = invoicePendingId, OwnerId = ownerId, RoomId = Guid.NewGuid(), OccupancyId = Guid.NewGuid(),
                RoomName = "R3", BuildingName = "B1", TenantName = "T3", Period = "2026-10", Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Issued, Total = 200_000, Paid = 0, Status = InvoiceStatus.Unpaid
            });
            db.PayOsAttempts.Add(new PayOsAttempt
            {
                OwnerId = ownerId, InvoiceId = invoicePendingId, OrderCode = orderPending, Amount = 200_000,
                Status = PaymentAttemptStatus.Pending, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            });
            handler.AddOrder(orderPending, 200_000, "PENDING", 0, ChecksumKeyA);

            await db.SaveChangesAsync();
        }

        // Execute batch reconciliation sweep
        using (var scope = app.Services.CreateScope())
        {
            var payOsService = scope.ServiceProvider.GetRequiredService<PayOsService>();
            var (checkedCount, failedCount) = await payOsService.ReconcilePendingBatchAsync(CancellationToken.None);

            Assert(checkedCount == 3, "batch reconciliation sweep checked all 3 pending orders");
            Assert(failedCount == 0, "batch reconciliation sweep executed with 0 failures");
        }

        // Verify database state after reconciliation sweep
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Order 1 was PAID: attempt marked Paid, invoice credited, receipt recorded
            var attempt1 = await db.PayOsAttempts.SingleAsync(a => a.OrderCode == orderPaid);
            var inv1 = await db.Invoices.FindAsync(invoicePaidId);
            Assert(attempt1.Status == PaymentAttemptStatus.Paid && inv1!.Paid == 300_000 && inv1.Status == InvoiceStatus.Paid,
                "reconciled PAID order marked Paid, invoice credited and Paid");
            Assert(await db.Receipts.AnyAsync(r => r.InvoiceId == invoicePaidId && r.Source == "payOS"),
                "reconciled PAID order generated payOS receipt");

            // Order 2 was CANCELLED: attempt marked Failed, invoice remains unpaid
            var attempt2 = await db.PayOsAttempts.SingleAsync(a => a.OrderCode == orderCancelled);
            var inv2 = await db.Invoices.FindAsync(invoiceCancelledId);
            Assert(attempt2.Status == PaymentAttemptStatus.Failed && attempt2.ProviderStatus == "CANCELLED" && inv2!.Paid == 0,
                "reconciled CANCELLED order marked Failed with zero paid credit");

            // Order 3 was PENDING: attempt remains Pending
            var attempt3 = await db.PayOsAttempts.SingleAsync(a => a.OrderCode == orderPending);
            Assert(attempt3.Status == PaymentAttemptStatus.Pending,
                "reconciled PENDING order remains Pending");
        }
    }

    private static async Task DatabaseIntegrityVerificationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        using var db = fixture.CreateDbContext();

        // Populate realistic records
        var owner = new AppUser { Id = Guid.NewGuid(), Name = "Integrity Owner", Email = "owner@integrity.local", PasswordHash = "unused", Role = UserRole.Owner };
        var building = new Building { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Integrity Tower" };
        var room = new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "Room I-1", Rent = 2_000_000, ElectricityRate = 3_000, WaterFee = 50_000, ServiceFee = 0, InitialReading = 0 };
        var occupancy = new Occupancy { Id = Guid.NewGuid(), RoomId = room.Id, TenantName = "Integrity Tenant", StartDate = new DateOnly(2026, 1, 1), People = 1, IsActive = true };

        db.Users.Add(owner);
        db.Buildings.Add(building);
        db.Rooms.Add(room);
        db.Occupancies.Add(occupancy);
        await db.SaveChangesAsync();

        // Execute database and relation integrity check
        await using var conn = fixture.OpenConnection();
        await using var integrityCmd = conn.CreateCommand();
        integrityCmd.CommandText = "SELECT 1;";
        var integrityResult = await integrityCmd.ExecuteScalarAsync();
        Assert(integrityResult != null, "database integrity check returns non-null");

        var loaded = await db.Occupancies.SingleAsync(o => o.Id == occupancy.Id);
        var loadedRoom = await db.Rooms.SingleAsync(r => r.Id == loaded.RoomId);
        var loadedBuilding = await db.Buildings.SingleAsync(b => b.Id == loadedRoom.BuildingId);
        Assert(loadedBuilding.OwnerId == owner.Id, "database relational hierarchy verified");
    }

    private static object BuildWebhookPayload(long orderCode, int amount, string reference, string checksumKey)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["code"] = "00",
            ["orderCode"] = orderCode.ToString(CultureInfo.InvariantCulture),
            ["reference"] = reference
        };
        var signature = PayOsSignature.Create(checksumKey, data);
        return new { success = true, data, signature };
    }

    private static WebApplication CreateTestApp(string connectionString, FakePayOsReconcileHandler handler)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "http://localhost",
            ["WebhookBaseUrl"] = "https://callbacks.example.invalid",
            ["RequireVerifiedPaymentForTenantLink"] = "false"
        });

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        builder.Services.AddSingleton<TenantTokenService>();
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddSingleton<IHttpClientFactory>(handler);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<PayOsService>(sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var prot = sp.GetRequiredService<IDataProtectionProvider>();
            var conf = sp.GetRequiredService<IConfiguration>();
            return new PayOsService(db, prot, handler, conf, NullLogger<PayOsService>.Instance);
        });

        builder.Services.AddRateLimiter(options => options.AddPolicy("tenant-payment",
            _ => RateLimitPartition.GetNoLimiter("fixture")));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapPaymentEndpoints();

        return app;
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class FakePayOsReconcileHandler : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<long, (int Amount, string Status, int AmountPaid, string ChecksumKey)> _orders = new();

        public void AddOrder(long orderCode, int amount, string status, int amountPaid, string checksumKey)
        {
            _orders[orderCode] = (amount, status, amountPaid, checksumKey);
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.StartsWith("/v2/payment-requests/"))
            {
                var orderCodeStr = request.RequestUri.AbsolutePath[("/v2/payment-requests/".Length)..];
                if (long.TryParse(orderCodeStr, out var orderCode) && _orders.TryGetValue(orderCode, out var info))
                {
                    var data = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["amount"] = info.Amount.ToString(CultureInfo.InvariantCulture),
                        ["amountPaid"] = info.AmountPaid.ToString(CultureInfo.InvariantCulture),
                        ["id"] = $"link-{orderCode}",
                        ["orderCode"] = orderCode.ToString(CultureInfo.InvariantCulture),
                        ["status"] = info.Status
                    };
                    var signature = PayOsSignature.Create(info.ChecksumKey, data);
                    var body = JsonSerializer.Serialize(new { code = "00", data, signature });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"code\":\"404\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}
