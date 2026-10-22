using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;
using RoomRental.Tests;

await using var testDb = await PostgresTestDb.CreateAsync(ensureCreated: true);
using var db = testDb.CreateDbContext();
var protection = new EphemeralDataProtectionProvider();
var protector = protection.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
var owner = new OwnerPaymentSettings { OwnerId = Guid.NewGuid(), ClientId = "fixture-client",
    ProtectedApiKey = protector.Protect("fixture-api"), ProtectedChecksumKey = protector.Protect("fixture-checksum"),
    UpdatedAt = DateTimeOffset.UtcNow };
db.PaymentSettings.Add(owner);
db.SaveChanges();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["WebhookBaseUrl"] = "https://callbacks.example.com" }).Build();
var handler = new FakeHandler();
var service = new PaymentWebhookService(db, protection, handler, config);
var callback = $"https://callbacks.example.com/api/payments/webhook/{owner.OwnerId}";

void Assert(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
}

handler.Body = JsonSerializer.Serialize(new { code = "00", data = new { webhookUrl = callback } });
var result = await service.ConfirmAsync(owner.OwnerId, default);
Assert(result.WebhookStatus == "Verified" && result.WebhookVerifiedAt != null, "successful confirmation");
using (var anotherDb = testDb.CreateDbContext())
{
    var anotherService = new PaymentWebhookService(anotherDb, protection, handler, config);
    Assert((await anotherService.GetAsync(owner.OwnerId, default)).WebhookStatus == "Verified", "verification persists across service instances");
}
Assert(handler.Calls == 1 && handler.RequestValid, "fixed official endpoint, correct credentials and callback payload");
handler.Status = HttpStatusCode.BadRequest;
handler.Body = "{\"code\":\"400\"}";
result = await service.ConfirmAsync(owner.OwnerId, default);
Assert(result.Configured && result.WebhookStatus == "Failed" && result.WebhookError != null && result.WebhookVerifiedAt == null,
    "rejection preserves settings and clears verified status");
handler.Status = HttpStatusCode.OK;
handler.Body = "not-json";
Assert((await service.ConfirmAsync(owner.OwnerId, default)).WebhookStatus == "Failed", "malformed response is never verified");
handler.Timeout = true;
Assert((await service.ConfirmAsync(owner.OwnerId, default)).WebhookStatus == "Failed", "timeout permits retry without losing keys");
handler.Timeout = false;
handler.Body = "{\"code\":\"00\",\"data\":{\"webhookUrl\":\"https://wrong.example.com\"}}";
Assert((await service.ConfirmAsync(owner.OwnerId, default)).WebhookStatus == "Failed", "mismatched callback is rejected");
handler.Body = JsonSerializer.Serialize(new { code = "00", data = new { webhookUrl = callback } });
await service.ConfirmAsync(owner.OwnerId, default);
config["WebhookBaseUrl"] = "https://new.example.com";
Assert((await service.GetAsync(owner.OwnerId, default)).WebhookStatus == "Unverified", "changed callback origin invalidates verification");
config["WebhookBaseUrl"] = "https://callbacks.example.com";
owner.ProtectedChecksumKey = protector.Protect("changed-fixture-checksum");
db.SaveChanges();
Assert((await service.GetAsync(owner.OwnerId, default)).WebhookStatus == "Unverified", "changed credentials invalidate verification");
config["WebhookBaseUrl"] = "http://localhost:5173";
var calls = handler.Calls;
result = await service.ConfirmAsync(owner.OwnerId, default);
Assert(result.WebhookStatus == "Failed" && result.WebhookUrl == null && handler.Calls == calls,
    "non-public HTTP callback fails before external call");
Assert(await db.Receipts.CountAsync() == 0, "confirmation never creates financial receipts");

await OperationalHardeningTests.RunAsync();
await SharedSupportTests.RunAsync();
await MigrationTests.RunAsync();
await PayOsPaymentTests.RunAsync();
await BatchPaymentTests.RunAsync();
await TenantLinkHttpTests.RunAsync();

AccountOccupancyTests.Run();
RegistrationEmailTemplateTests.Run();
await RegistrationVerificationTests.RunAsync();
await PasswordRecoveryTests.RunAsync();
await ManualReceiptTests.RunAsync();
await ManualPaymentAssessmentTests.RunAsync();
await AuthSecurityHttpTests.RunAsync();
await RoomTenantLifecycleHttpTests.RunAsync();
await AccountingBillingHttpTests.RunAsync();
await PayOsOperationsHttpTests.RunAsync();
await MonitoringFoundationTests.RunAsync();
await MonitoringBusinessTests.RunAsync();
await MonitoringAccuracyAndOutboxRegressionTests.RunAsync();

await RoomRental.WebhookTests.OperationsMonitoringTests.RunAsync();
await RoomRental.WebhookTests.MonitoringSchedulerTests.RunAsync();
await PromotionsTests.RunAsync();

sealed class FakeHandler : HttpMessageHandler, IHttpClientFactory
{
    public string Body = "{}";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public bool Timeout;
    public int Calls;
    public bool RequestValid;
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        RequestValid = request.RequestUri!.AbsoluteUri == "https://api-merchant.payos.vn/confirm-webhook" &&
            request.Method == HttpMethod.Post && request.Headers.GetValues("x-client-id").Single() == "fixture-client" &&
            request.Headers.GetValues("x-api-key").Single() == "fixture-api" &&
            payload.RootElement.GetProperty("webhookUrl").GetString()!.StartsWith("https://callbacks.example.com/api/payments/webhook/");
        if (Timeout) throw new TaskCanceledException();
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}
