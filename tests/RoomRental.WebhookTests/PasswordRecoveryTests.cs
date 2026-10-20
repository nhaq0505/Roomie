using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

internal static class PasswordRecoveryTests
{
    public static async Task RunAsync()
    {
        using (var f = new Fixture())
        {
            var challenge = await f.Start();
            var row = await f.Db.PasswordRecoveries.AsNoTracking().SingleAsync();
            Check(f.Sender.Code is null && row.ProtectedCode is not null && row.ChallengeHash != challenge.ChallengeToken,
                "request queues encrypted delivery without exposing account existence or waiting for SMTP");
            Check((await f.Db.AuthSessions.SingleAsync()).RevokedAt is null && f.User.PasswordResetHash is null,
                "requesting recovery never logs out or locks a legitimate account");
            await f.Deliver();
            var code = f.Sender.Code!;
            Check(code.Length == 6 && (await f.Db.PasswordRecoveries.AsNoTracking().SingleAsync()).ProtectedCode is null,
                "background delivery decrypts only its claimed code and removes encrypted payload");
            await f.Deliver(); Check(f.Sender.Deliveries == 1, "claimed email is not redelivered");
            await Reject(() => f.Service.CompleteAsync(challenge.ChallengeToken, "newpassword", "newpassword", default), 400);
            var grant = await f.Service.VerifyAsync(challenge.ChallengeToken, code, default);
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, code, default), 400);
            await Reject(() => f.Service.CompleteAsync(grant.ResetToken, "12345678", "12345678", default), 400);
            await Reject(() => f.Service.CompleteAsync(grant.ResetToken, "newpassword", "different", default), 400);
            await f.Service.CompleteAsync(grant.ResetToken, "newpassword", "newpassword", default);
            Check(f.Hasher.VerifyHashedPassword(f.User, f.User.PasswordHash, "newpassword") != PasswordVerificationResult.Failed,
                "verified grant changes the intended password");
            Check((await f.Db.AuthSessions.SingleAsync()).RevokedAt is not null, "completion revokes all sessions");
            await Reject(() => f.Service.CompleteAsync(grant.ResetToken, "anotherpassword", "anotherpassword", default), 400);
            await f.Deliver(); Check(f.Sender.Notifications == 1, "password-change notification is queued after success");
            await f.Deliver(); Check(f.Sender.Notifications == 1, "notification is claimed once");
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start("missing@example.invalid");
            await f.Deliver(); Check(f.Sender.Code is null, "unknown accounts receive the same challenge shape but no email");
            await Reject(() => f.Start("missing@example.invalid"), 429);
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, "123456", default), 400);
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start(); await f.Deliver(); var code = f.Sender.Code!;
            for (var i = 0; i < 5; i++) await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, "bad", default), 400);
            Check((await f.Db.PasswordRecoveries.SingleAsync()).FailedAttempts == 5, "wrong-code attempts survive rejected responses");
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, code, default), 400);
            await Reject(() => f.Start(), 429);
            f.Clock.Advance(TimeSpan.FromSeconds(61)); var replacement = await f.Start(); await f.Deliver();
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, code, default), 400);
            for (var i = 0; i < 3; i++) { f.Clock.Advance(TimeSpan.FromSeconds(61)); await f.Start(); }
            f.Clock.Advance(TimeSpan.FromSeconds(61)); await Reject(() => f.Start(), 429);
            f.Clock.Advance(TimeSpan.FromHours(1)); await f.Start();
            Check((await f.Db.PasswordRecoveries.SingleAsync()).SendCount == 1, "hourly send cap persists across replacement challenges");
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start(); await f.Deliver(); var code = f.Sender.Code!;
            f.Clock.Advance(TimeSpan.FromMinutes(10));
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, code, default), 400);
            challenge = await f.Start(); await f.Deliver();
            var grant = await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code!, default);
            f.Clock.Advance(TimeSpan.FromMinutes(10));
            await Reject(() => f.Service.CompleteAsync(grant.ResetToken, "newpassword", "newpassword", default), 400);
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start(); await f.Deliver();
            var grant = await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code!, default);
            f.User.PasswordHash = f.Hasher.HashPassword(f.User, "otherpassword"); await f.Db.SaveChangesAsync();
            await Reject(() => f.Service.CompleteAsync(grant.ResetToken, "newpassword", "newpassword", default), 400);
            f.Clock.Advance(TimeSpan.FromSeconds(61)); challenge = await f.Start(); await f.Deliver();
            f.User.PasswordResetHash = PasswordSecurity.HashToken("manual-admin-code"); await f.Db.SaveChangesAsync();
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code!, default), 400);
            Check(true, "password changes and admin recovery invalidate earlier email recovery proofs");
        }
        using (var f = new Fixture())
        {
            f.User.Role = UserRole.Admin; await f.Db.SaveChangesAsync();
            var challenge = await f.Start(); await f.Deliver();
            var grant = await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code!, default);
            await f.Service.CompleteAsync(grant.ResetToken, "adminnewpassword", "adminnewpassword", default);
            Check(f.User.Role == UserRole.Admin, "email recovery supports admin without changing permissions");
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start(); f.Sender.Fail = true;
            await Reject(() => f.Deliver(), 503);
            Check((await f.Db.PasswordRecoveries.AsNoTracking().SingleAsync()).ProtectedCode is null && f.User.PasswordResetHash is null,
                "SMTP failure neither locks an account nor queues unbounded repeat emails");
            f.Sender.Fail = false; f.Clock.Advance(TimeSpan.FromSeconds(61));
            challenge = await f.Start(); await f.Deliver();
            await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code!, default);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS recovery: " + message); }
    private static async Task Reject(Func<Task> action, int status)
    {
        try { await action(); } catch (ApiException exception) when (exception.StatusCode == status) { return; }
        throw new Exception($"Expected recovery rejection {status}");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class Sender : IPasswordRecoveryEmailSender
    {
        public string? Code; public int Deliveries; public int Notifications; public bool Fail;
        public void EnsureConfigured() { }
        public Task SendRecoveryCodeAsync(string email, string code, CancellationToken ct)
        {
            if (Fail) throw new ApiException(503, "Synthetic SMTP failure");
            Code = code; Deliveries++; return Task.CompletedTask;
        }
        public Task SendPasswordChangedAsync(string email, CancellationToken ct) { Notifications++; return Task.CompletedTask; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; }
        public Sender Sender { get; } = new();
        public Clock Clock { get; } = new();
        public PasswordHasher<AppUser> Hasher { get; } = new();
        public EphemeralDataProtectionProvider Protection { get; } = new();
        public AppUser User { get; }
        public PasswordRecoveryService Service { get; }
        public Fixture()
        {
            connection.Open();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); Db.Database.EnsureCreated();
            User = new AppUser { Name = "Fixture", Email = "recovery@example.invalid", PasswordHash = "" };
            User.PasswordHash = Hasher.HashPassword(User, "oldpassword");
            Db.Users.Add(User); Db.AuthSessions.Add(new AuthSession { Id = "fixture-session", UserId = User.Id, ExpiresAt = Clock.GetUtcNow().AddDays(1) }); Db.SaveChanges();
            Service = new(Db, Sender, Protection, Hasher, Clock);
        }
        public Task<PasswordRecoveryChallengeDto> Start(string email = "recovery@example.invalid") => Service.StartAsync(email, default);
        public Task Deliver() => PasswordRecoveryEmailWorker.DeliverPendingAsync(Db, Sender, Protection, Clock, default);
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
}
