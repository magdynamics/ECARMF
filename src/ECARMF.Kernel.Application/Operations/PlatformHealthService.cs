using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Integrations;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Compliance;

namespace ECARMF.Kernel.Application.Operations;

/// <summary>One client's operational posture, at a glance.</summary>
public record TenantHealth(
    string TenantId,
    string Name,
    string Status,
    int CriticalAlertsOpen,
    int WarningAlertsOpen,
    int OpenTasks,
    int RenewalsOverdue,
    int RenewalsDueSoon,
    int FeedsFailing,
    int RecordsThisMonth,
    int DocumentsThisMonth,
    DateTimeOffset? LastFeedRun);

public interface IPlatformHealthService
{
    /// <summary>The operator's one screen: every client tenant with its open
    /// alarms, work, approaching lapses, feed health, and month-to-date
    /// volume — worst first.</summary>
    Task<IReadOnlyList<TenantHealth>> GetHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Replaces checking tenants one by one: a single pass over the client
/// portfolio surfacing who needs attention today. Sorted so the loudest
/// problems (critical alerts, overdue renewals, failing feeds) rise to the
/// top of the board.
/// </summary>
public class PlatformHealthService : IPlatformHealthService
{
    private readonly ITenantDirectory _tenants;
    private readonly IDeviationStore _deviations;
    private readonly ITaskStore _tasks;
    private readonly IRenewalStore _renewals;
    private readonly IIntegrationStore _integrations;
    private readonly IUsageMeter _usage;

    public PlatformHealthService(
        ITenantDirectory tenants, IDeviationStore deviations, ITaskStore tasks,
        IRenewalStore renewals, IIntegrationStore integrations, IUsageMeter usage)
    {
        _tenants = tenants;
        _deviations = deviations;
        _tasks = tasks;
        _renewals = renewals;
        _integrations = integrations;
        _usage = usage;
    }

    public async Task<IReadOnlyList<TenantHealth>> GetHealthAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var board = new List<TenantHealth>();

        foreach (var tenant in await _tenants.GetAllAsync(ct))
        {
            var alerts = (await _deviations.GetRecentAsync(tenant.TenantId, 200, ct))
                .Where(d => d.ResolvedAt is null)
                .ToList();

            var openTasks = (await _tasks.GetRecentAsync(tenant.TenantId, 200, ct))
                .Count(t => t.Status == "Open");

            var activeRenewals = (await _renewals.GetAllAsync(tenant.TenantId, ct))
                .Where(r => r.Status == RenewalStatuses.Active)
                .ToList();

            // A feed is failing when its most recent run failed.
            var integrations = await _integrations.GetAllAsync(tenant.TenantId, ct);
            var runs = await _integrations.GetRunsAsync(tenant.TenantId, null, 100, ct);
            var feedsFailing = integrations.Count(i =>
                runs.Where(r => r.IntegrationId == i.IntegrationId)
                    .OrderByDescending(r => r.StartedAt)
                    .FirstOrDefault() is { Success: false });
            var lastRun = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault()?.StartedAt;

            var usage = await _usage.MeasureAsync(tenant.TenantId, monthStart, now, ct);

            board.Add(new TenantHealth(
                tenant.TenantId,
                tenant.Name,
                tenant.Status,
                alerts.Count(a => a.Severity == "Critical"),
                alerts.Count(a => a.Severity == "Warning"),
                openTasks,
                activeRenewals.Count(r => r.DueDate < now),
                activeRenewals.Count(r => r.DueDate >= now && r.DueDate <= now.AddDays(30)),
                feedsFailing,
                usage.RecordsProcessed,
                usage.DocumentsArchived,
                lastRun));
        }

        return board
            .OrderByDescending(t => t.CriticalAlertsOpen + t.RenewalsOverdue + t.FeedsFailing)
            .ThenByDescending(t => t.WarningAlertsOpen + t.RenewalsDueSoon)
            .ThenBy(t => t.Name)
            .ToList();
    }
}
