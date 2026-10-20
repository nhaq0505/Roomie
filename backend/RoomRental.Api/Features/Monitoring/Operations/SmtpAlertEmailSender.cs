using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace RoomRental.Api.Features.Monitoring.Operations;

public interface IAlertEmailSender
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default);
}

public sealed class SmtpAlertEmailSender : IAlertEmailSender
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SmtpAlertEmailSender> _logger;

    public SmtpAlertEmailSender(
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<SmtpAlertEmailSender> logger)
    {
        _config = config;
        _environment = environment;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var host = _config["Email:Smtp:Host"] ?? Environment.GetEnvironmentVariable("SMTP_HOST");
        var portStr = _config["Email:Smtp:Port"] ?? Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
        var securityStr = _config["Email:Smtp:Security"] ?? Environment.GetEnvironmentVariable("SMTP_SECURITY") ?? "StartTls";
        var username = _config["Email:Smtp:Username"] ?? Environment.GetEnvironmentVariable("SMTP_USERNAME");
        var password = _config["Email:Smtp:Password"] ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD");

        var fromAddress = _config["Monitoring:Alerts:AlertEmailFrom"]
            ?? _config["Email:FromAddress"]
            ?? Environment.GetEnvironmentVariable("SMTP_FROM_ADDRESS")
            ?? "noreply@roomie.local";

        var fromName = _config["Monitoring:Alerts:AlertEmailFromName"]
            ?? _config["Email:FromName"]
            ?? Environment.GetEnvironmentVariable("SMTP_FROM_NAME")
            ?? "Roomie Monitoring";

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("SMTP host is not configured (Email:Smtp:Host / SMTP_HOST).");
        }

        if (string.IsNullOrWhiteSpace(to) || !MailboxAddress.TryParse(to, out var toMailbox))
        {
            throw new InvalidOperationException("Alert recipient address is invalid or not configured.");
        }

        if (string.IsNullOrWhiteSpace(fromAddress) || !MailboxAddress.TryParse(fromAddress, out var fromMailbox))
        {
            throw new InvalidOperationException("Alert sender address (From) is invalid or not configured.");
        }

        if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
        {
            port = 587;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(toMailbox);
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient { Timeout = 15000 };
            var security = Enum.TryParse<SecureSocketOptions>(securityStr, ignoreCase: true, out var sec)
                ? sec
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(host, port, security, timeoutCts.Token);

            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password ?? "", timeoutCts.Token);
            }

            await client.SendAsync(message, timeoutCts.Token);

            try
            {
                await client.DisconnectAsync(true, timeoutCts.Token);
            }
            catch
            {
                // Disconnect error after accepted send is non-fatal
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is IOException or SocketException or MailKit.CommandException or MailKit.ProtocolException or
                System.Security.Authentication.AuthenticationException or MailKit.Security.AuthenticationException or OperationCanceledException)
        {
            _logger.LogWarning("Monitoring alert SMTP delivery failed: {ErrorType}", ex.GetType().Name);
            throw new InvalidOperationException($"Monitoring alert SMTP delivery failed ({ex.GetType().Name}).", ex);
        }
    }
}
