using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The action center ranks the operator's cross-tenant to-do; a
/// wrong urgency ordering buries the thing that actually needed attention.</summary>
public class PlatformActionTests
{
    private static (PlatformActionService Service, StubPlatformRisk Risk,
        InMemoryRenewalStore Renewals, InMemoryTenantDirectory Tenants) Build()
    {
        var risk = new StubPlatformRisk();
        var renewals = new InMemoryRenewalStore();
        var tenants = new InMemoryTenantDirectory();
        tenants.Items.Add(new TenantProfile { TenantId = "t1", Name = "Tenant One" });
        return (new PlatformActionService(risk, renewals, tenants), risk, renewals, tenants);
    }

    private static RenewalCommitment Renewal(string name, int dueInDays, string status = RenewalStatuses.Active) => new()
    {
        TenantId = "t1", Name = name, Category = "License",
        DueDate = DateTimeOffset.UtcNow.AddDays(dueInDays), Status = status, CreatedBy = "op"
    };

    [Theory]
    [InlineData(-3, 100)]  // overdue → act now
    [InlineData(5, 92)]    // within a week
    [InlineData(15, 75)]   // within three weeks
    [InlineData(40, 58)]   // on the horizon
    public async Task Renewal_urgency_scales_with_time_to_due(int dueInDays, int expectedUrgency)
    {
        var (service, _, renewals, _) = Build();
        renewals.Items.Add(Renewal("Item", dueInDays));

        var item = (await service.ListAsync()).Items.Single();

        Assert.Equal(expectedUrgency, item.Urgency);
        Assert.Equal("Renewal", item.Type);
        Assert.Equal("Tenant One", item.TenantName);
    }

    [Fact]
    public async Task Renewals_beyond_45_days_and_inactive_ones_are_left_off_the_queue()
    {
        var (service, _, renewals, _) = Build();
        renewals.Items.Add(Renewal("Far future", 90));
        renewals.Items.Add(Renewal("Renewed already", 3, RenewalStatuses.Renewed));

        Assert.Empty((await service.ListAsync()).Items);
    }

    [Fact]
    public async Task Critical_risk_urgency_grows_with_the_count_and_caps_at_100()
    {
        var (service, risk, _, _) = Build();
        risk.Overview = new PlatformRiskOverview(30, 15, 2, 5,
        [
            new TenantRiskSummary("t1", "Tenant One", 10, 2, 20, ["Cyber"]),      // 55 + 8 = 63
            new TenantRiskSummary("t2", "Tenant Two", 20, 13, 25, ["Ops"]),       // 55 + 52 → capped 100
        ], []);

        var items = (await service.ListAsync()).Items;

        Assert.Equal(100, items.Single(i => i.TenantId == "t2").Urgency);
        Assert.Equal(63, items.Single(i => i.TenantId == "t1").Urgency);
        Assert.All(items, i => Assert.Equal("Critical risk", i.Type));
    }

    [Fact]
    public async Task Queue_is_ranked_most_urgent_first_and_urgent_counts_at_90_plus()
    {
        var (service, risk, renewals, _) = Build();
        renewals.Items.Add(Renewal("Overdue license", -2));   // 100
        renewals.Items.Add(Renewal("Due soon", 6));           // 92
        renewals.Items.Add(Renewal("Watch item", 30));        // 58
        risk.Overview = new PlatformRiskOverview(5, 1, 1, 5,
            [new TenantRiskSummary("t1", "Tenant One", 5, 1, 20, ["Cyber"])], []); // 59

        var result = await service.ListAsync();

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Urgent); // the 100 and the 92
        Assert.True(result.Items.Zip(result.Items.Skip(1))
            .All(pair => pair.First.Urgency >= pair.Second.Urgency), "queue not sorted by urgency desc");
        Assert.Equal("Overdue license", result.Items[0].Title);
    }

    [Fact]
    public async Task Tenants_with_zero_critical_risks_produce_no_risk_action()
    {
        var (service, risk, _, _) = Build();
        risk.Overview = new PlatformRiskOverview(8, 0, 1, 5,
            [new TenantRiskSummary("t1", "Tenant One", 8, 0, 12, ["Ops"])], []);

        Assert.Empty((await service.ListAsync()).Items);
    }
}
