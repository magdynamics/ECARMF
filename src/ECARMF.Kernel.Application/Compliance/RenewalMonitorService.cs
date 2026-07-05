using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Compliance;

/// <summary>Tenant-scoped renewal commitment store.</summary>
public interface IRenewalStore
{
    Task<RenewalCommitment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RenewalCommitment>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<RenewalCommitment>> GetActiveAllTenantsAsync(CancellationToken ct = default);
    Task AddAsync(RenewalCommitment renewal, CancellationToken ct = default);
    Task UpdateAsync(RenewalCommitment renewal, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default);
}

public interface IRenewalMonitor
{
    /// <summary>Walks every Active commitment (all tenants when tenantId is
    /// null) and raises the alert ladder for any whose due date has come
    /// close enough. Returns the number of alerts raised. Idempotent per
    /// ladder rung: re-evaluating the same day never re-alerts.</summary>
    Task<int> EvaluateAsync(string? tenantId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Marks a commitment renewed: a recurring one advances its due
    /// date by the recurrence and resets its ladder; a one-time one
    /// completes. Audited either way.</summary>
    Task<RenewalCommitment?> MarkRenewedAsync(
        string tenantId, Guid id, string actor, CancellationToken ct = default);
}

/// <summary>
/// The calendar watchdog. Failure-to-renew (a lapsed license, an uninsured
/// day, a missed loan installment) is pure avoidable risk, so every Active
/// commitment gets an escalating warning ladder: Info at the earliest lead
/// time, Warning in the middle rungs (optionally opening a renewal task),
/// Critical at the last rung and once overdue. Every escalation raises a
/// DeviationAlert (dashboard feed) and a notification, and is audited —
/// silence is never the response to an approaching lapse.
/// </summary>
public class RenewalMonitorService : IRenewalMonitor
{
    private readonly IRenewalStore _renewals;
    private readonly IDeviationStore _alerts;
    private readonly INotificationStore _notifications;
    private readonly ITaskStore _tasks;
    private readonly IAuditLog _audit;

    public RenewalMonitorService(
        IRenewalStore renewals,
        IDeviationStore alerts,
        INotificationStore notifications,
        ITaskStore tasks,
        IAuditLog audit)
    {
        _renewals = renewals;
        _alerts = alerts;
        _notifications = notifications;
        _tasks = tasks;
        _audit = audit;
    }

    public async Task<int> EvaluateAsync(string? tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var candidates = (tenantId is null
            ? await _renewals.GetActiveAllTenantsAsync(ct)
            : (await _renewals.GetAllAsync(tenantId, ct))
                .Where(r => r.Status == RenewalStatuses.Active).ToList())
            // Milestone-gated obligations (Rosetta Requirement 5) stay
            // dormant until their milestone is reached — a certificate of
            // occupancy cannot be "overdue" while the building has no roof.
            .Where(r => r.MilestoneReference is null || r.MilestoneReachedAt is not null)
            .ToList();

        var raised = 0;
        foreach (var renewal in candidates)
        {
            var rung = DeepestRungReached(renewal, now);
            if (rung is null || !IsDeeper(rung.Value, renewal.LastAlertedThresholdDays))
            {
                continue;
            }

            await RaiseAsync(renewal, rung.Value, now, ct);
            renewal.LastAlertedThresholdDays = rung.Value;
            renewal.UpdatedAt = now;
            await _renewals.UpdateAsync(renewal, ct);
            raised++;
        }

        return raised;
    }

    public async Task<RenewalCommitment?> MarkRenewedAsync(
        string tenantId, Guid id, string actor, CancellationToken ct = default)
    {
        var renewal = await _renewals.GetAsync(tenantId, id, ct);
        if (renewal is null || renewal.Status != RenewalStatuses.Active)
        {
            return renewal;
        }

        var now = DateTimeOffset.UtcNow;
        var previousDue = renewal.DueDate;
        if (renewal.RecurrenceMonths is int months and > 0)
        {
            // Advance from the scheduled date, not from today: renewing early
            // must not silently shorten the next cycle.
            renewal.DueDate = renewal.DueDate.AddMonths(months);
            renewal.LastAlertedThresholdDays = null;
            // A unit-accruing obligation (CPE/CLE) starts the next licensing
            // period from zero.
            renewal.CompletedUnits = 0;
        }
        else
        {
            renewal.Status = RenewalStatuses.Renewed;
        }

        renewal.RenewalCount++;
        renewal.LastRenewedAt = now;
        renewal.UpdatedAt = now;
        await _renewals.UpdateAsync(renewal, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = renewal.TenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.RenewalCompleted,
            Actor = actor,
            Summary = $"Renewal '{renewal.Name}' completed"
                + (renewal.Status == RenewalStatuses.Active
                    ? $"; next due {renewal.DueDate:yyyy-MM-dd}."
                    : " (one-time obligation closed)."),
            Detail = new Dictionary<string, string>
            {
                ["renewalId"] = renewal.Id.ToString(),
                ["name"] = renewal.Name,
                ["category"] = renewal.Category,
                ["previousDueDate"] = previousDue.ToString("O"),
                ["nextDueDate"] = renewal.Status == RenewalStatuses.Active ? renewal.DueDate.ToString("O") : "",
                ["renewalCount"] = renewal.RenewalCount.ToString(CultureInfo.InvariantCulture)
            }
        }, ct);

        return renewal;
    }

    /// <summary>The deepest ladder rung the calendar has reached: -1 when
    /// overdue, otherwise the smallest configured lead time that is at or
    /// past due-in days; null while the due date is still beyond the ladder.</summary>
    internal static int? DeepestRungReached(RenewalCommitment renewal, DateTimeOffset now)
    {
        var daysUntil = (int)Math.Ceiling((renewal.DueDate - now).TotalDays);
        if (daysUntil < 0)
        {
            return -1;
        }

        var reached = renewal.LeadTimeDays.Where(t => t >= 0 && daysUntil <= t).ToList();
        return reached.Count == 0 ? null : reached.Min();
    }

    /// <summary>Has the ladder escalated past what was already alerted?</summary>
    internal static bool IsDeeper(int rung, int? lastAlerted)
        => lastAlerted is null || rung < lastAlerted.Value;

    /// <summary>Severity by rung position: earliest lead time Info, middle
    /// rungs Warning, the last rung and overdue Critical.</summary>
    internal static string SeverityFor(RenewalCommitment renewal, int rung)
    {
        if (rung < 0)
        {
            return "Critical";
        }

        var ladder = renewal.LeadTimeDays.Where(t => t >= 0).Distinct().OrderByDescending(t => t).ToList();
        if (ladder.Count == 0 || rung == ladder[^1])
        {
            return "Critical";
        }

        return rung == ladder[0] && ladder.Count > 1 ? "Info" : "Warning";
    }

    private async Task RaiseAsync(RenewalCommitment renewal, int rung, DateTimeOffset now, CancellationToken ct)
    {
        var daysUntil = (int)Math.Ceiling((renewal.DueDate - now).TotalDays);
        var severity = SeverityFor(renewal, rung);
        var correlationId = Guid.NewGuid();
        var subject = renewal.Counterparty is null ? renewal.Name : $"{renewal.Name} ({renewal.Counterparty})";
        var message = rung < 0
            ? $"OVERDUE: renewal '{subject}' was due {renewal.DueDate:yyyy-MM-dd} and has not been renewed — the {renewal.Category.ToLowerInvariant()} may have lapsed."
            : $"Renewal '{subject}' is due {renewal.DueDate:yyyy-MM-dd} — {daysUntil} day(s) left.";

        await _alerts.AddAsync(new DeviationAlert
        {
            TenantId = renewal.TenantId,
            EntityReference = $"renewal:{renewal.Id}",
            MetricType = $"RenewalDue.{renewal.Category}",
            ActualValue = daysUntil,
            ExpectedValue = rung < 0 ? 0 : rung,
            ExpectedValueSource = "Renewal",
            VarianceMagnitude = daysUntil,
            ThresholdBreached = rung,
            Severity = severity,
            CorrelationId = correlationId
        }, ct);

        await _notifications.AddAsync(new NotificationItem
        {
            TenantId = renewal.TenantId,
            WorkflowId = $"renewal:{renewal.Id}",
            Target = renewal.NotifyRole,
            Message = message,
            Severity = severity,
            CorrelationId = correlationId
        }, ct);

        // One renewal task per cycle, opened at the first non-Info escalation.
        if (renewal.CreateTask && severity != "Info"
            && (renewal.LastAlertedThresholdDays is null
                || SeverityFor(renewal, renewal.LastAlertedThresholdDays.Value) == "Info"))
        {
            await _tasks.AddAsync(new TaskItem
            {
                TenantId = renewal.TenantId,
                WorkflowId = $"renewal:{renewal.Id}",
                Title = $"Renew {renewal.Category.ToLowerInvariant()}: {subject} (due {renewal.DueDate:yyyy-MM-dd}"
                    + (renewal.Reference is null ? ")" : $", ref {renewal.Reference})"),
                Assignee = renewal.NotifyRole,
                Severity = severity,
                CorrelationId = correlationId
            }, ct);
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = renewal.TenantId,
            CorrelationId = correlationId,
            Category = AuditCategories.RenewalAlertRaised,
            Actor = "system:flywheel",
            Summary = message,
            Detail = new Dictionary<string, string>
            {
                ["renewalId"] = renewal.Id.ToString(),
                ["name"] = renewal.Name,
                ["category"] = renewal.Category,
                ["dueDate"] = renewal.DueDate.ToString("O"),
                ["daysUntilDue"] = daysUntil.ToString(CultureInfo.InvariantCulture),
                ["ladderRung"] = rung.ToString(CultureInfo.InvariantCulture),
                ["severity"] = severity,
                ["notifyRole"] = renewal.NotifyRole
            }
        }, ct);
    }
}
