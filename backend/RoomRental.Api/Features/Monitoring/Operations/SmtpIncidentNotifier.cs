using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class IncidentNotificationOptions
{
    public const string SectionName = "Monitoring:Alerts";
    public string? AlertEmailTo { get; set; }
    public string AlertEmailFrom { get; set; } = "noreply@roomrental.local";
    public string AlertEmailFromName { get; set; } = "Roomie Monitoring";
    public bool Enabled { get; set; } = true;
}

public sealed class SmtpIncidentNotifier : IIncidentNotifier
{
    private readonly IOptions<IncidentNotificationOptions> _options;
    private readonly IAlertEmailSender _emailSender;
    private readonly ILogger<SmtpIncidentNotifier> _logger;

    public SmtpIncidentNotifier(
        IOptions<IncidentNotificationOptions> options,
        IAlertEmailSender emailSender,
        ILogger<SmtpIncidentNotifier> logger)
    {
        _options = options;
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _logger = logger;
    }

    public async Task SendIncidentAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct = default)
    {
        var opt = _options.Value;
        if (!opt.Enabled)
        {
            _logger.LogInformation("Incident alerting disabled via options.");
            throw new InvalidOperationException("Incident alerting disabled via configuration options.");
        }

        // An unset Compose variable may arrive as an empty section value.
        var recipient = string.IsNullOrWhiteSpace(opt.AlertEmailTo)
            ? Environment.GetEnvironmentVariable("MONITORING_ALERT_TO")
            : opt.AlertEmailTo;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("No alert recipient configured for incident {Key}", incident.IncidentKey);
            throw new InvalidOperationException($"No alert recipient configured for incident {incident.IncidentKey} (MONITORING_ALERT_TO / Monitoring:Alerts:AlertEmailTo).");
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

        // Never log recipient email address to protect PII
        _logger.LogInformation("Sending incident alert for {Key} (isRecovery: {Recovery})",
            incident.IncidentKey, isRecovery);

        await _emailSender.SendEmailAsync(recipient, subject, body, ct);
    }

    public Task SendAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct = default)
        => SendIncidentAlertAsync(incident, isRecovery, ct);
}
