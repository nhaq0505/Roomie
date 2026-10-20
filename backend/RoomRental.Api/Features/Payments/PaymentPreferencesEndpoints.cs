using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class PaymentPreferencesEndpoints
{
    private static readonly JsonElement Banks = JsonDocument.Parse(typeof(PaymentPreferencesEndpoints).Assembly
        .GetManifestResourceStream("RoomRental.Payments.Banks.json")!).RootElement.Clone();
    public static void MapPaymentPreferencesEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api").RequireAuthorization(p => p.RequireRole("Owner"));
        routes.MapGet("/settings/payment/banks", () => Results.Ok(Banks));
        routes.MapPut("/settings/payment/preferences", async (PaymentPreferencesRequest request, HttpContext context,
            AppDbContext db, PaymentWebhookService webhookService, CancellationToken ct) =>
        {
            if (request.ConfirmationMode is not ("Automatic" or "Manual")) throw new ApiException(400, "Chế độ xác nhận thanh toán không hợp lệ.");
            var bin = request.BankBin?.Trim(); var number = request.AccountNumber?.Trim(); var name = request.AccountName?.Trim();
            if (request.ConfirmationMode == "Manual" &&
                (!Banks.EnumerateArray().Any(b => b.GetProperty("bin").GetString() == bin) ||
                 string.IsNullOrEmpty(number) || !Regex.IsMatch(number, "^[A-Za-z0-9]{4,34}$") ||
                 string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsControl)))
                throw new ApiException(400, "Vui lòng nhập ngân hàng, số tài khoản và tên chủ tài khoản hợp lệ.");
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var ownerId = ApiSupport.OwnerId(context);
            var owner = await db.Users.SingleAsync(x => x.Id == ownerId, ct);
            if (await db.PayOsAttempts.AnyAsync(x => x.OwnerId == ownerId && x.Status == PaymentAttemptStatus.Pending, ct))
                throw new ApiException(409, "Vui lòng đối soát các giao dịch payOS đang chờ trước khi đổi thông tin thanh toán.");
            owner.PaymentConfirmationMode = Enum.Parse<PaymentConfirmationMode>(request.ConfirmationMode);
            if (owner.PaymentConfirmationMode == PaymentConfirmationMode.Manual) {
                owner.ManualBankBin = bin; owner.ManualAccountNumber = number; owner.ManualAccountName = name;
            }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(await webhookService.GetAsync(ownerId, ct));
        });
    }
}
