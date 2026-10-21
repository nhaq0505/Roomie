using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class RegistrationVerificationTests
{
    public static async Task RunAsync()
    {
        using (var f = new Fixture())
        {
            var challenge = await f.Start();
            Check(challenge.VerificationRequired && !await f.Db.Users.AnyAsync(), "OTP start never creates a login-capable user");
            var pending = await f.Db.RegistrationVerifications.AsNoTracking().SingleAsync();
            Check(pending.ChallengeHash.Length == 64 && pending.ChallengeHash != challenge.ChallengeToken &&
                pending.CodeHash.Length == 64 && pending.CodeHash != f.Sender.Code && pending.PasswordHash != Fixture.Password,
                "only challenge/code/password hashes are stored");
            await Reject(() => f.Service.VerifyAsync(new string('A', 64), f.Sender.Code, default), 400, "OTP is bound to its unguessable challenge token");
            var user = await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code, default);
            Check(user.Email == "otp@example.invalid" && user.Status == OwnerStatus.Approved && user.Role == UserRole.Owner &&
                new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, Fixture.Password) != PasswordVerificationResult.Failed,
                "correct OTP creates the intended Free owner with the submitted password");
            Check((await f.Db.RegistrationVerifications.SingleAsync()).PasswordHash == "", "consumption clears pending password/code hashes");
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code, default), 400, "OTP cannot be replayed");
            await Reject(() => f.Start(), 409, "verified email cannot be replaced through registration");
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start();
            var old = f.Sender.Code;
            for (var i = 0; i < 5; i++) await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, "not-otp", default), 400, "invalid OTP attempt is rejected");
            Check((await f.Db.RegistrationVerifications.AsNoTracking().SingleAsync()).FailedAttempts == 5, "failed attempt counter commits despite HTTP error");
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, old, default), 400, "fifth failed attempt locks even the correct code");
            await Reject(() => f.Service.ResendAsync(challenge.ChallengeToken, default), 429, "resend cooldown is enforced on server");
            f.Clock.Advance(TimeSpan.FromSeconds(61));
            await f.Service.ResendAsync(challenge.ChallengeToken, default);
            Check(f.Sender.Code != old, "resend rotates the OTP");
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, old, default), 400, "resend invalidates the earlier OTP");
            await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code, default);
            Check(await f.Db.Users.CountAsync() == 1, "a new OTP can unlock verification without duplicate users");
        }
        using (var f = new Fixture())
        {
            var challenge = await f.Start();
            f.Clock.Advance(TimeSpan.FromMinutes(10));
            await Reject(() => f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code, default), 400, "OTP expires exactly at ten minutes");
            await f.Service.ResendAsync(challenge.ChallengeToken, default);
            await f.Service.VerifyAsync(challenge.ChallengeToken, f.Sender.Code, default);
            Check(await f.Db.Users.AnyAsync(), "resending an expired code within its registration window works");
        }
        using (var f = new Fixture())
        {
            var original = await f.Start();
            await Reject(() => f.Start(), 429, "restarting registration cannot bypass email cooldown");
            f.Clock.Advance(TimeSpan.FromSeconds(61));
            var replacement = await f.Start();
            await Reject(() => f.Service.VerifyAsync(original.ChallengeToken, f.Sender.Code, default), 400, "new registration invalidates an older challenge");
            for (var i = 0; i < 3; i++) { f.Clock.Advance(TimeSpan.FromSeconds(61)); await f.Service.ResendAsync(replacement.ChallengeToken, default); }
            f.Clock.Advance(TimeSpan.FromSeconds(61));
            await Reject(() => f.Service.ResendAsync(replacement.ChallengeToken, default), 429, "five-send hourly cap survives re-registration");
            f.Clock.Advance(TimeSpan.FromMinutes(31));
            await Reject(() => f.Service.ResendAsync(replacement.ChallengeToken, default), 400, "expired registration cannot be extended indefinitely");
            f.Clock.Advance(TimeSpan.FromHours(1));
            await f.Start();
            Check((await f.Db.RegistrationVerifications.SingleAsync()).SendCount == 1, "new hourly window resets the email send budget");
        }
        using (var f = new Fixture())
        {
            f.Sender.Fail = true;
            await Reject(() => f.Start(), 503, "SMTP failure is reported explicitly");
            Check(!await f.Db.Users.AnyAsync() && !await f.Db.RegistrationVerifications.AsNoTracking().AnyAsync(), "SMTP failure leaves no account or committed challenge");
            f.Db.ChangeTracker.Clear(); f.Sender.Fail = false;
            var challenge = await f.Start(); var originalCode = f.Sender.Code;
            f.Clock.Advance(TimeSpan.FromSeconds(61)); f.Sender.Fail = true;
            await Reject(() => f.Service.ResendAsync(challenge.ChallengeToken, default), 503, "failed resend is rejected");
            f.Db.ChangeTracker.Clear();
            await f.Service.VerifyAsync(challenge.ChallengeToken, originalCode, default);
            Check(await f.Db.Users.CountAsync() == 1, "failed resend preserves the last delivered OTP");
        }
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Email:Smtp:Host"] = "localhost", ["Email:Smtp:Port"] = "2525", ["Email:Smtp:Security"] = "None", ["Email:FromAddress"] = "otp@example.invalid"
        }).Build();
        var env = new HostingEnvironment { EnvironmentName = Environments.Production };
        var smtp = new SmtpRegistrationEmailSender(settings, env, NullLogger<SmtpRegistrationEmailSender>.Instance);
        await Reject(() => { smtp.EnsureConfigured(); return Task.CompletedTask; }, 503, "Production rejects plaintext SMTP");
        env.EnvironmentName = Environments.Development;
        smtp.EnsureConfigured();
        Check(true, "loopback plaintext SMTP is available only for Development fixture delivery");
        settings["Email:Smtp:Host"] = "remote.example.invalid";
        await Reject(() => { smtp.EnsureConfigured(); return Task.CompletedTask; }, 503, "Development also rejects plaintext remote SMTP");
    }

    private static async Task Reject(Func<Task> action, int status, string label)
    {
        try { await action(); }
        catch (ApiException e) when (e.StatusCode == status) { Check(true, label); return; }
        throw new Exception(label);
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
    private sealed class Fixture : IDisposable
    {
        public const string Password = "OtpFixturePassword123!";
        private readonly PostgresTestDb testDb;
        public AppDbContext Db { get; }
        public Clock Clock { get; } = new();
        public Sender Sender { get; } = new();
        public RegistrationVerificationService Service { get; }
        public Fixture()
        {
            testDb = PostgresTestDb.CreateSync(ensureCreated: true);
            Db = testDb.CreateDbContext();
            Sender.CheckTransaction = () => Db.Database.CurrentTransaction is null;
            Service = new(Db, Sender, Clock);
        }
        public Task<RegistrationVerificationDto> Start()
        {
            var user = new AppUser { Name = "OTP fixture", Email = "otp@example.invalid", PasswordHash = "" };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
            return Service.StartAsync(user, default);
        }
        public void Dispose() { Db.Dispose(); testDb.Dispose(); }
    }
    private sealed class Sender : IRegistrationEmailSender
    {
        public string Code = "";
        public bool Fail;
        public Func<bool>? CheckTransaction;
        public void EnsureConfigured() { }
        public Task SendAsync(string email, string code, CancellationToken ct)
        {
            if (CheckTransaction?.Invoke() != true) throw new Exception("SMTP must run outside a database transaction");
            if (Fail) throw new ApiException(503, "Synthetic SMTP failure");
            Code = code; return Task.CompletedTask;
        }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
