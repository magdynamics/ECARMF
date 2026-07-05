using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryRenewalStore : IRenewalStore
{
    public List<RenewalCommitment> Items { get; } = [];

    public Task<RenewalCommitment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.TenantId == tenantId && r.Id == id));

    public Task<IReadOnlyList<RenewalCommitment>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RenewalCommitment>>(Items.Where(r => r.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<RenewalCommitment>> GetActiveAllTenantsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RenewalCommitment>>(
            Items.Where(r => r.Status == RenewalStatuses.Active).ToList());

    public Task AddAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        Items.Add(renewal);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        var index = Items.FindIndex(r => r.TenantId == renewal.TenantId && r.Id == renewal.Id);
        if (index >= 0) Items[index] = renewal;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        Items.RemoveAll(r => r.TenantId == tenantId && r.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>The calendar watchdog: escalating alerts as due dates approach,
/// idempotent per rung, Critical once overdue, renewal advances the cycle.</summary>
public class RenewalMonitorTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryRenewalStore _renewals = new();
    private readonly InMemoryDeviationStore _alerts = new();
    private readonly InMemoryNotificationStore _notifications = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly RenewalMonitorService _monitor;

    public RenewalMonitorTests()
    {
        _monitor = new RenewalMonitorService(_renewals, _alerts, _notifications, _tasks, _audit);
    }

    private RenewalCommitment Add(DateTimeOffset due, int? recurrence = 12, params int[] ladder)
    {
        var renewal = new RenewalCommitment
        {
            TenantId = Tenant,
            Name = "GL insurance policy",
            Category = RenewalCategories.Insurance,
            Counterparty = "Acme Insurance",
            Reference = "POL-100",
            DueDate = due,
            RecurrenceMonths = recurrence,
            LeadTimeDays = ladder.Length > 0 ? ladder : new[] { 90, 30, 7 },
            NotifyRole = "ExecutiveOwner",
            CreateTask = true,
            CreatedBy = "owner@tenant-a"
        };
        _renewals.Items.Add(renewal);
        return renewal;
    }

    [Fact]
    public async Task Far_future_due_date_stays_silent()
    {
        var now = DateTimeOffset.UtcNow;
        Add(now.AddDays(120));

        var raised = await _monitor.EvaluateAsync(Tenant, now);

        Assert.Equal(0, raised);
        Assert.Empty(_notifications.Items);
        Assert.Empty(_alerts.Items);
    }

    [Fact]
    public async Task First_rung_alerts_info_and_is_idempotent()
    {
        var now = DateTimeOffset.UtcNow;
        Add(now.AddDays(80)); // inside 90, outside 30

        Assert.Equal(1, await _monitor.EvaluateAsync(Tenant, now));
        Assert.Equal(0, await _monitor.EvaluateAsync(Tenant, now)); // same rung: silent

        var notification = Assert.Single(_notifications.Items);
        Assert.Equal("Info", notification.Severity);
        Assert.Contains("80 day(s) left", notification.Message);
        Assert.Empty(_tasks.Items); // Info does not open a task
        Assert.Single(_audit.Items.Where(a => a.Category == AuditCategories.RenewalAlertRaised));
    }

    [Fact]
    public async Task Ladder_escalates_warning_then_critical_and_opens_one_task()
    {
        var start = DateTimeOffset.UtcNow;
        Add(start.AddDays(80));

        await _monitor.EvaluateAsync(Tenant, start);                 // 80d -> Info
        await _monitor.EvaluateAsync(Tenant, start.AddDays(55));     // 25d -> Warning
        await _monitor.EvaluateAsync(Tenant, start.AddDays(75));     // 5d  -> Critical
        await _monitor.EvaluateAsync(Tenant, start.AddDays(75));     // idempotent

        Assert.Equal(3, _notifications.Items.Count);
        Assert.Equal(new[] { "Info", "Warning", "Critical" },
            _notifications.Items.Select(n => n.Severity).ToArray());
        Assert.Single(_tasks.Items); // one renewal task per cycle
        Assert.StartsWith("Renew insurance:", Assert.Single(_tasks.Items).Title);
    }

    [Fact]
    public async Task Overdue_commitment_raises_critical_alarm()
    {
        var now = DateTimeOffset.UtcNow;
        Add(now.AddDays(-3));

        Assert.Equal(1, await _monitor.EvaluateAsync(Tenant, now));

        var notification = Assert.Single(_notifications.Items);
        Assert.Equal("Critical", notification.Severity);
        Assert.Contains("OVERDUE", notification.Message);
        var alert = Assert.Single(_alerts.Items);
        Assert.Equal("Renewal", alert.ExpectedValueSource);
        Assert.Equal("RenewalDue.Insurance", alert.MetricType);
    }

    [Fact]
    public async Task Mark_renewed_advances_recurring_cycle_and_resets_ladder()
    {
        var now = DateTimeOffset.UtcNow;
        var renewal = Add(now.AddDays(5), recurrence: 12);
        await _monitor.EvaluateAsync(Tenant, now); // Critical fired

        var updated = await _monitor.MarkRenewedAsync(Tenant, renewal.Id, "owner@tenant-a");

        Assert.NotNull(updated);
        Assert.Equal(RenewalStatuses.Active, updated!.Status);
        Assert.Equal(renewal.DueDate, updated.DueDate); // instance mutated in place
        Assert.True(updated.DueDate > now.AddDays(300)); // advanced ~12 months
        Assert.Null(updated.LastAlertedThresholdDays);
        Assert.Equal(1, updated.RenewalCount);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.RenewalCompleted);

        // Next cycle is quiet again until its own ladder is reached.
        Assert.Equal(0, await _monitor.EvaluateAsync(Tenant, now));
    }

    [Fact]
    public async Task Mark_renewed_closes_one_time_obligation()
    {
        var now = DateTimeOffset.UtcNow;
        var renewal = Add(now.AddDays(5), recurrence: null);

        var updated = await _monitor.MarkRenewedAsync(Tenant, renewal.Id, "owner@tenant-a");

        Assert.Equal(RenewalStatuses.Renewed, updated!.Status);
        Assert.Equal(0, await _monitor.EvaluateAsync(Tenant, now)); // no longer monitored
    }

    [Fact]
    public async Task Evaluation_is_tenant_scoped_but_all_tenants_pass_covers_everyone()
    {
        var now = DateTimeOffset.UtcNow;
        Add(now.AddDays(5));
        _renewals.Items.Add(new RenewalCommitment
        {
            TenantId = "tenant-b",
            Name = "Business license",
            Category = RenewalCategories.License,
            DueDate = now.AddDays(5),
            LeadTimeDays = new[] { 30, 7 },
            NotifyRole = "ExecutiveOwner",
            CreatedBy = "owner@tenant-b"
        });

        Assert.Equal(1, await _monitor.EvaluateAsync(Tenant, now));
        Assert.Equal(1, await _monitor.EvaluateAsync(null, now)); // picks up tenant-b only
        Assert.Equal(2, _notifications.Items.Select(n => n.TenantId).Distinct().Count());
    }
}
