using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Notifications;

/// <summary>An outbound email, provider-agnostic.</summary>
public record EmailMessage(
    IReadOnlyList<string> To, string Subject, string Body);

/// <summary>Transport port: SMTP in production, a fake in tests. The
/// platform stays independent — mail goes through whatever SMTP server the
/// operator points it at (on-prem relay, office mail server).</summary>
public interface IEmailSender
{
    Task SendAsync(MailDeliverySettings settings, EmailMessage message, CancellationToken ct = default);
}

/// <summary>Platform-level mail configuration, set by the operator.</summary>
public record MailDeliverySettings(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string? Username,
    string? Password,
    string FromAddress,
    /// <summary>Info | Warning | Critical — the least severe notification
    /// that still gets emailed. Default Warning keeps Info advisories
    /// in-app only.</summary>
    string MinSeverity);

public record MailSettingsStatus(
    bool Configured, bool Enabled, string? Host, int? Port, bool? UseSsl,
    string? Username, string? FromAddress, string? MinSeverity,
    string? ConfiguredBy, DateTimeOffset? UpdatedAt);

public interface IMailSettingsStore
{
    Task<MailDeliverySettings?> GetAsync(CancellationToken ct = default);
    Task<MailSettingsStatus> GetStatusAsync(CancellationToken ct = default);
    Task SetAsync(MailDeliverySettings settings, string configuredBy, CancellationToken ct = default);
}

/// <summary>A notification awaiting email delivery, and the outcome marker.
/// Delivery state lives on the notification row so nothing is emailed twice
/// and failures stay visible.</summary>
public interface INotificationOutbox
{
    /// <summary>Notifications not yet emailed (regardless of severity —
    /// the dispatcher filters), oldest first, across all tenants.</summary>
    Task<IReadOnlyList<NotificationItem>> GetPendingAsync(int limit, CancellationToken ct = default);

    /// <summary>Records the delivery outcome: recipients on success, the
    /// error on failure, or "skipped: below severity threshold". Either way
    /// the notification leaves the pending set.</summary>
    Task MarkProcessedAsync(Guid notificationId, string outcome, CancellationToken ct = default);
}

/// <summary>
/// Carries alarms out of the app: any notification at or above the
/// configured severity is emailed to the tenant users holding the targeted
/// role; a tenant with no matching addresses falls back to its primary
/// contact. Deliveries and failures are recorded per notification and
/// audited — an alarm that could not leave the building is itself visible.
/// </summary>
public class NotificationEmailService
{
    private static readonly string[] SeverityOrder = ["Info", "Warning", "Critical"];

    private readonly INotificationOutbox _outbox;
    private readonly IMailSettingsStore _settings;
    private readonly IEmailSender _sender;
    private readonly IUserStore _users;
    private readonly ITenantDirectory _tenants;
    private readonly IAuditLog _audit;

    public NotificationEmailService(
        INotificationOutbox outbox,
        IMailSettingsStore settings,
        IEmailSender sender,
        IUserStore users,
        ITenantDirectory tenants,
        IAuditLog audit)
    {
        _outbox = outbox;
        _settings = settings;
        _sender = sender;
        _users = users;
        _tenants = tenants;
        _audit = audit;
    }

    /// <summary>One dispatch pass. Returns the number of emails sent.
    /// Does nothing (and leaves the queue untouched) until mail delivery
    /// is configured and enabled, so history is emailed once settings
    /// arrive — never silently discarded.</summary>
    public async Task<int> ProcessPendingAsync(int limit = 50, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        if (settings is null || !settings.Enabled)
        {
            return 0;
        }

        var pending = await _outbox.GetPendingAsync(limit, ct);
        var sent = 0;
        foreach (var notification in pending)
        {
            if (!MeetsThreshold(notification.Severity, settings.MinSeverity))
            {
                await _outbox.MarkProcessedAsync(notification.Id,
                    $"skipped: {notification.Severity} below threshold {settings.MinSeverity}", ct);
                continue;
            }

            var recipients = await ResolveRecipientsAsync(notification.TenantId, notification.Target, ct);
            if (recipients.Count == 0)
            {
                await _outbox.MarkProcessedAsync(notification.Id,
                    $"skipped: no email address for role {notification.Target} and no tenant contact", ct);
                continue;
            }

            try
            {
                await _sender.SendAsync(settings, BuildMessage(notification, recipients), ct);
                await _outbox.MarkProcessedAsync(notification.Id, $"sent: {string.Join(", ", recipients)}", ct);
                sent++;

                await _audit.AppendAsync(new AuditEntry
                {
                    TenantId = notification.TenantId,
                    CorrelationId = notification.CorrelationId,
                    Category = AuditCategories.NotificationEmailed,
                    Actor = "system:flywheel",
                    Summary = $"{notification.Severity} notification emailed to {recipients.Count} recipient(s).",
                    Detail = new Dictionary<string, string>
                    {
                        ["notificationId"] = notification.Id.ToString(),
                        ["recipients"] = string.Join(", ", recipients),
                        ["severity"] = notification.Severity,
                        ["target"] = notification.Target
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                // The failure is recorded on the notification (visible to the
                // operator) and the queue moves on — one bad address must not
                // dam the whole outbox.
                await _outbox.MarkProcessedAsync(notification.Id, $"failed: {ex.Message}", ct);
            }
        }

        return sent;
    }

    internal static bool MeetsThreshold(string severity, string minSeverity)
    {
        var actual = Array.IndexOf(SeverityOrder, severity);
        var min = Array.IndexOf(SeverityOrder, minSeverity);
        if (min < 0) min = 1; // unknown threshold: default Warning
        return actual < 0 || actual >= min; // unknown severities err on delivering
    }

    /// <summary>Tenant users holding the targeted role (with an email on
    /// file), or the tenant's primary contact as fallback.</summary>
    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        string tenantId, string targetRole, CancellationToken ct)
    {
        var users = await _users.GetAllAsync(tenantId, ct);
        var addresses = users
            .Where(u => !u.IsSystemActor
                && !string.IsNullOrWhiteSpace(u.Email)
                && string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && u.Roles.Contains(targetRole, StringComparer.OrdinalIgnoreCase))
            .Select(u => u.Email!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (addresses.Count > 0)
        {
            return addresses;
        }

        var profile = await _tenants.GetAsync(tenantId, ct);
        return string.IsNullOrWhiteSpace(profile?.ContactEmail)
            ? Array.Empty<string>()
            : new[] { profile!.ContactEmail!.Trim() };
    }

    private static EmailMessage BuildMessage(NotificationItem notification, IReadOnlyList<string> recipients)
    {
        var subject = $"[ECARMF {notification.Severity}] {Truncate(notification.Message, 120)}";
        var body =
            $"{notification.Message}\r\n\r\n" +
            $"Severity: {notification.Severity}\r\n" +
            $"Tenant: {notification.TenantId}\r\n" +
            $"For role: {notification.Target}\r\n" +
            $"Raised: {notification.CreatedAt:yyyy-MM-dd HH:mm} UTC\r\n" +
            $"Correlation: {notification.CorrelationId}\r\n\r\n" +
            "Open the ECARMF platform for details, evidence, and the related task.";
        return new EmailMessage(recipients, subject, body);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}
