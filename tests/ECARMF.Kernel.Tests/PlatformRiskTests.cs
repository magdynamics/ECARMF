using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The platform risk roll-up drives the operator's cross-tenant
/// view: dedup to the latest score per risk, severity/likelihood extraction,
/// and the critical-zone rule must hold.</summary>
public class PlatformRiskTests
{
    private static (PlatformRiskService Service, InMemoryScoreStore Scores, InMemoryTenantDirectory Tenants) Build()
    {
        var scores = new InMemoryScoreStore();
        var tenants = new InMemoryTenantDirectory();
        tenants.Items.Add(new TenantProfile { TenantId = "t1", Name = "Tenant One" });
        tenants.Items.Add(new TenantProfile { TenantId = "t2", Name = "Tenant Two" });
        return (new PlatformRiskService(scores, tenants), scores, tenants);
    }

    private static ScoreRecord Risk(string tenant, string subject, string domain,
        int sev, int like, DateTimeOffset? at = null, bool withMetadata = true) => new()
    {
        TenantId = tenant,
        SubjectType = "Performance",
        SubjectId = subject,
        ScoreType = "KPIActual",
        Value = sev * like,
        RiskType = domain,
        ComputedAt = at ?? DateTimeOffset.UtcNow,
        Metadata = withMetadata
            ? new Dictionary<string, string> { ["severityValue"] = sev.ToString(), ["likelihood"] = like.ToString() }
            : []
    };

    [Fact]
    public async Task One_risk_per_subject_using_the_latest_score()
    {
        var (service, scores, _) = Build();
        var old = DateTimeOffset.UtcNow.AddDays(-10);
        scores.Items.Add(Risk("t1", "risk-a", "Cyber", 5, 5, old));            // superseded
        scores.Items.Add(Risk("t1", "risk-a", "Cyber", 2, 2));                 // latest wins
        scores.Items.Add(Risk("t1", "risk-b", "Cyber", 3, 3));

        var overview = await service.OverviewAsync();

        Assert.Equal(2, overview.TotalRisks);
        Assert.Equal(0, overview.CriticalRisks); // the 5x5 reading was superseded by 2x2
    }

    [Fact]
    public async Task Severity_and_likelihood_come_from_metadata_when_present()
    {
        var (service, scores, _) = Build();
        scores.Items.Add(Risk("t1", "risk-a", "Cyber", 4, 5));

        var p = (await service.OverviewAsync()).Heatmap.Single();

        Assert.Equal(4, p.Severity);
        Assert.Equal(5, p.Likelihood);
        Assert.Equal(20, p.Index);
    }

    [Fact]
    public async Task Without_metadata_the_rating_is_derived_from_the_value_and_clamped()
    {
        var (service, scores, _) = Build();
        var s = Risk("t1", "risk-a", "Ops", 0, 0, withMetadata: false);
        s.Value = 20; // sqrt(20)≈4.47 → severity 4, likelihood round(20/4)=5
        scores.Items.Add(s);
        var huge = Risk("t1", "risk-b", "Ops", 0, 0, withMetadata: false);
        huge.Value = 500; // derivation must clamp into 1..5
        scores.Items.Add(huge);

        var points = (await service.OverviewAsync()).Heatmap;

        var derived = points.Single(p => p.Label.Contains("Ops") && p.Index == 20);
        Assert.Equal(4, derived.Severity);
        Assert.Equal(5, derived.Likelihood);
        Assert.All(points, p => { Assert.InRange(p.Severity, 1, 5); Assert.InRange(p.Likelihood, 1, 5); });
    }

    [Fact]
    public async Task Critical_zone_requires_high_severity_AND_high_likelihood()
    {
        var (service, scores, _) = Build();
        scores.Items.Add(Risk("t1", "a", "Cyber", 5, 5)); // critical
        scores.Items.Add(Risk("t1", "b", "Cyber", 4, 4)); // critical (boundary)
        scores.Items.Add(Risk("t1", "c", "Cyber", 5, 3)); // severe but unlikely — not critical
        scores.Items.Add(Risk("t1", "d", "Cyber", 3, 5)); // likely but mild — not critical

        var overview = await service.OverviewAsync();

        Assert.Equal(2, overview.CriticalRisks);
    }

    [Fact]
    public async Task Tenant_summaries_rank_by_critical_count_then_volume()
    {
        var (service, scores, _) = Build();
        scores.Items.Add(Risk("t1", "a", "Cyber", 5, 5));
        scores.Items.Add(Risk("t2", "b", "Ops", 2, 2));
        scores.Items.Add(Risk("t2", "c", "Ops", 2, 3));

        var overview = await service.OverviewAsync();

        Assert.Equal("t1", overview.Tenants[0].TenantId);       // 1 critical beats 2 non-critical
        Assert.Equal("Tenant One", overview.Tenants[0].Name);   // display name resolved
        Assert.Equal(2, overview.TenantsWithRisk);
        Assert.Equal(2, overview.TenantsTotal);
        Assert.Contains("Ops", overview.Tenants[1].TopDomains);
    }
}
