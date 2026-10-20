using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class IncidentNotificationOptions
{
    public const string SectionName = "Monitoring:Alerts";
    public string? AlertEmailTo { get; set; }
    public string AlertEmailFrom { get; set; } = "noreply@roomrental.local";
    public bool Enabled { get; set; } = true;
}

public interface IAlertEmailSender
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default);
}

public sealed class SmtpIncidentNotifier : IIncidentAlertNotifier
{
    private readonly IAlertEmailSender? _emailSender;
    private readonly IncidentNotificationOptions _options;
    private readonly ILogger<SmtpIncidentNotifier> _logger;

    public SmtpIncidentNotifier(
        IOptions<IncidentNotificationOptions> options,
        ILogger<SmtpIncidentNotifier> logger,
        IAlertEmailSender? emailSender = null)
    {
        _options = options.Value;
        _logger = logger;
        _emailSender = emailSender;
    }

    public async Task SendAlertAsync(IncidentSnapshot incident, bool isRecovery, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Incident alerts disabled by configuration");
            return;
        }

        var recipient = _options.AlertEmailTo ?? Environment.GetEnvironmentVariable("MONITORING_ALERT_TO");
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("No alert recipient configured for incident {Key}", incident.IncidentKey);
            return;
        }

        var prefix = isRecovery ? "[RESOLVED]" : $"[ALERT - {incident.Severity.ToUpperInvariant()}]";
        var subject = $"{prefix} {incident.RuleName}";

        var body = $"""
            Incident: {incident.RuleName} ({incident.IncidentKey})
            Severity: {incident.Severity}
            Status: {(isRecovery ? "Resolved" : incident.State)}
            Triggered At: {incident.StartedAt:O}
            {(isRecovery ? $"Resolved At: {incident.ResolvedAt:O}\n" : "")}
            Notification #{incident.NotificationCount}

            Summary:
            {incident.Summary}

            Details:
            {incident.Details}

            ---
            Note: SMTP acceptance by server does not guarantee final mailbox delivery (accepted != delivered).
            """;

        _logger.LogInformation("Sending incident alert for {Key} (isRecovery: {Recovery}) to {Recipient}",
            incident.IncidentKey, isRecovery, recipient);

        if (_emailSender != null)
        {
            await _emailSender.SendEmailAsync(recipient, subject, body, ct);
        }
    }
}
