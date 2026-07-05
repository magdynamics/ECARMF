using ECARMF.Kernel.Application.Operations;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Integrations;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Domain.Workflow;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The portfolio board: every client's posture in one pass,
/// loudest problems first.</summary>
public class PlatformHealthTests
{
    private readonly FakeTenantDirectory _tenants = new();
    private readonly InMemoryDeviationStore _deviations = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryRenewalStore _renewals = new();
    private readonly InMemoryIntegrationStore _integrations = new();
    private readonly FakeUsageMeter _usage = new();
    private readonly PlatformHealthService _service;

    public PlatformHealthTests()
    {
        _service = new PlatformHealthService(
            _tenants, _deviations, _tasks, _renewals, _integrations, _usage);
    }

    private void AddTenant(string id, string name) =>
        _tenants.Profiles[id] = new TenantProfile { TenantId = id, Name = name, Status = "Active" };

    [Fact]
    public async Task Clients_with_loud_problems_sort_first()
    {
        AddTenant("calm-co", "Calm Co");
        AddTenant("burning-co", "Burning Co");
        _deviations.Items.Add(new DeviationAlert
        {
            TenantId = "burning-co", Severity = "Critical", MetricType = "GPPercent"
        });
        _renewals.Items.Add(new RenewalCommitment
        {
            TenantId = "burning-co", Name = "License", Category = RenewalCategories.License,
            DueDate = DateTimeOffset.UtcNow.AddDays(-2), NotifyRole = "ExecutiveOwner", CreatedBy = "u"
        });

        var board = await _service.GetHealthAsync();

        Assert.Equal("burning-co", board[0].TenantId);
        Assert.Equal(1, board[0].CriticalAlertsOpen);
        Assert.Equal(1, board[0].RenewalsOverdue);
        Assert.Equal("calm-co", board[1].TenantId);
        Assert.Equal(0, board[1].CriticalAlertsOpen);
    }

    [Fact]
    public async Task Resolved_alerts_and_completed_work_do_not_count()
    {
        AddTenant("tidy-co", "Tidy Co");
        _deviations.Items.Add(new DeviationAlert
        {
            TenantId = "tidy-co", Severity = "Critical", ResolvedAt = DateTimeOffset.UtcNow
        });
        _tasks.Items.Add(new TaskItem { TenantId = "tidy-co", Title = "done", Status = "Completed" });

        var health = Assert.Single(await _service.GetHealthAsync());

        Assert.Equal(0, health.CriticalAlertsOpen);
        Assert.Equal(0, health.OpenTasks);
    }

    [Fact]
    public async Task A_feed_whose_latest_run_failed_counts_as_failing()
    {
        AddTenant("feed-co", "Feed Co");
        _integrations.Items.Add(new IntegrationDefinition
        {
            TenantId = "feed-co", IntegrationId = "pos-feed", Name = "POS"
        });
        _integrations.Runs.Add(new FeedRun
        {
            TenantId = "feed-co", IntegrationId = "pos-feed",
            Success = false, StartedAt = DateTimeOffset.UtcNow
        });
        _integrations.Runs.Add(new FeedRun
        {
            TenantId = "feed-co", IntegrationId = "pos-feed",
            Success = true, StartedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });

        var health = Assert.Single(await _service.GetHealthAsync());

        Assert.Equal(1, health.FeedsFailing); // latest run failed, older success irrelevant
        Assert.NotNull(health.LastFeedRun);
    }
}
