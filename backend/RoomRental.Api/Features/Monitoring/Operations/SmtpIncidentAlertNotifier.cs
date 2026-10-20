using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class SmtpIncidentAlertNotifier(
    IOptions<IncidentNotificationOptions> options,
    ILogger<SmtpIncidentAlertNotifier> logger,
    IAlertEmailSender? emailSender = null) : IIncidentNotifier
{
    public async Task SendIncidentAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Incident alerting disabled via options.");
            return;
        }

        var recipient = opt.AlertEmailTo ?? Environment.GetEnvironmentVariable("MONITORING_ALERT_TO");
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("No alert recipient configured for incident {Key}", incident.IncidentKey);
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

        logger.LogInformation("Sending incident alert for {Key} (isRecovery: {Recovery}) to {Recipient}",
            incident.IncidentKey, isRecovery, recipient);

        if (emailSender != null)
        {
            await emailSender.SendEmailAsync(recipient, subject, body, ct);
        }
    }
}
