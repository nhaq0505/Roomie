using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace RoomRental.Api.Services;

public interface IRegistrationEmailSender
{
    void EnsureConfigured();
    Task SendAsync(string email, string code, CancellationToken ct);
}

public interface IPasswordRecoveryEmailSender
{
    void EnsureConfigured();
    Task SendRecoveryCodeAsync(string email, string code, CancellationToken ct);
    Task SendPasswordChangedAsync(string email, CancellationToken ct);
}

public sealed class SmtpRegistrationEmailSender(IConfiguration config, IHostEnvironment environment,
    ILogger<SmtpRegistrationEmailSender> logger) : IRegistrationEmailSender, IPasswordRecoveryEmailSender
{
    private const string Unavailable = "Chức năng gửi email chưa sẵn sàng. Vui lòng thử lại sau hoặc liên hệ quản trị viên.";

    public void EnsureConfigured()
    {
        var host = config["Email:Smtp:Host"];
        var security = config["Email:Smtp:Security"] ?? "StartTls";
        var localTest = environment.IsDevelopment() && host is "localhost" or "127.0.0.1" or "::1";
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(config["Email:FromAddress"]) || !MailboxAddress.TryParse(config["Email:FromAddress"], out _) ||
            !int.TryParse(config["Email:Smtp:Port"] ?? "587", out var port) || port is < 1 or > 65535 ||
            (security != "StartTls" && security != "SslOnConnect" && !(security == "None" && localTest)))
            throw new ApiException(503, Unavailable);
    }

    public Task SendAsync(string email, string code, CancellationToken ct) =>
        DeliverAsync(email, "Mã xác nhận đăng ký Roomie", RegistrationEmailTemplate.CreateBody(code), ct);

    public Task SendRecoveryCodeAsync(string email, string code, CancellationToken ct) =>
        DeliverAsync(email, "Mã đặt lại mật khẩu Roomie", RegistrationEmailTemplate.CreateRecoveryBody(code), ct);

    public Task SendPasswordChangedAsync(string email, CancellationToken ct) =>
        DeliverAsync(email, "Mật khẩu Roomie đã được thay đổi", new TextPart("plain")
        {
            Text = "Mật khẩu tài khoản Roomie của bạn vừa được đặt lại. Các phiên đăng nhập cũ đã kết thúc. Nếu bạn không thực hiện thao tác này, hãy liên hệ quản trị viên ngay. Không chia sẻ mật khẩu hoặc OTP với bất kỳ ai."
        }, ct);

    private async Task DeliverAsync(string email, string subject, MimeEntity body, CancellationToken ct)
    {
        EnsureConfigured();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config["Email:FromName"] ?? "Roomie", config["Email:FromAddress"]!));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            message.Body = body;
            using var client = new SmtpClient { Timeout = 15000 };
            var security = config["Email:Smtp:Security"] ?? "StartTls";
            await client.ConnectAsync(config["Email:Smtp:Host"]!, int.Parse(config["Email:Smtp:Port"] ?? "587"),
                Enum.Parse<SecureSocketOptions>(security), timeout.Token);
            var username = config["Email:Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username))
                await client.AuthenticateAsync(username, config["Email:Smtp:Password"] ?? "", timeout.Token);
            await client.SendAsync(message, timeout.Token);
            // DATA was accepted: a QUIT failure must not invalidate an already delivered code.
            try { await client.DisconnectAsync(true, timeout.Token); }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        catch (Exception exception) when (!ct.IsCancellationRequested &&
            exception is IOException or System.Net.Sockets.SocketException or MailKit.CommandException or MailKit.ProtocolException or
                System.Security.Authentication.AuthenticationException or MailKit.Security.AuthenticationException or OperationCanceledException)
        {
            // Never log SMTP responses, credentials, recipient addresses or OTP values.
            logger.LogWarning("Account email delivery failed; check SMTP configuration and connectivity.");
            throw new ApiException(503, Unavailable);
        }
    }
}
