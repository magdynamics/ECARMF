using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Performance;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Batch 2 refinements: polymorphic KPI subjects (R7), knowledge
/// asset relationships/search flag (R8), investor gating semantics (R10),
/// riskType tag (R11), and source ownership (R12).</summary>
public class Batch2RefinementTests
{
    private const string Tenant = "batch2";

    [Fact]
    public async Task R7_employee_level_kpis_declare_and_stamp_their_subject_type()
    {
        var registries = new TenantRegistryProvider();
        var scores = new InMemoryScoreStore();
        var evaluator = new PerformanceEvaluationService(registries, scores, new InMemoryAuditLog());

        registries.GetFor(Tenant).PerformanceFrameworks.Register(new PerformanceFrameworkDeclaration
        {
            FrameworkId = "employee-comp-v1",
            Name = "Employee KPIs",
            Industry = "Services",
            Kpis =
            [
                new KPIDefinition
                {
                    KpiId = "billable-ratio", Formula = "billable / total",
                    TriggerRecordType = "TimeSheet", SubjectField = "employeeId",
                    SubjectType = "User", // Refinement 7: the subject is a PERSON
                    TargetValue = 0.7m, Direction = "higher"
                }
            ]
        }, "ecarmf.magdynamics-hr", "1.0.0");

        await evaluator.EvaluateAsync(new KernelEvent(
            Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "TimeSheet",
                ["employeeId"] = "amina@magdynamics.example",
                ["billable"] = "120",
                ["total"] = "160"
            },
            DateTimeOffset.UtcNow));

        var actual = Assert.Single(scores.Items, s => s.ScoreType == "KPIActual");
        Assert.Equal("billable-ratio@amina@magdynamics.example", actual.SubjectId);
        Assert.Equal("User", actual.Metadata["kpiSubjectType"]);
    }

    [Fact]
    public async Task R11_kpi_declared_risk_type_is_stamped_on_emitted_actuals()
    {
        var registries = new TenantRegistryProvider();
        var scores = new InMemoryScoreStore();
        var evaluator = new PerformanceEvaluationService(registries, scores, new InMemoryAuditLog());

        registries.GetFor(Tenant).PerformanceFrameworks.Register(new PerformanceFrameworkDeclaration
        {
            FrameworkId = "project-risk-v1", Name = "Project risks", Industry = "Technology",
            Kpis =
            [
                new KPIDefinition
                {
                    KpiId = "delay-days", Formula = "delayDays",
                    TriggerRecordType = "JiraProjectPeriod", SubjectField = "projectKey",
                    SubjectType = "Project", RiskType = "ProjectDelay",
                    TargetValue = 0, Direction = "lower"
                }
            ]
        }, "ecarmf.ai-magdynamics", "1.0.0");

        await evaluator.EvaluateAsync(new KernelEvent(
            Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "JiraProjectPeriod",
                ["projectKey"] = "ECARMF-PORTAL",
                ["delayDays"] = "12"
            },
            DateTimeOffset.UtcNow));

        var actual = Assert.Single(scores.Items, s => s.ScoreType == "KPIActual");
        Assert.Equal("ProjectDelay", actual.RiskType); // risk flags ride the KPI mechanism
    }

    [Fact]
    public void R8_knowledge_assets_carry_relationships_and_filter_by_asset_type()
    {
        var registry = new KnowledgeAssetRegistry();
        registry.Register(new KnowledgeAsset
        {
            AssetId = "sop-client-intake-v2", DocKey = "sop-client-intake",
            Title = "Client intake SOP", AssetType = "SOP",
            EffectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SemanticSearchEnabled = true,
            Relationships = [new KnowledgeAssetRelationship
                { RelatedAssetId = "policy-client-data", RelationshipType = "implements" }]
        }, "ecarmf.magdynamics-kb", "1.0.0");
        registry.Register(new KnowledgeAsset
        {
            AssetId = "policy-client-data", DocKey = "policy-client-data",
            Title = "Client data handling policy", AssetType = "PolicyDocument",
            EffectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }, "ecarmf.magdynamics-kb", "1.0.0");

        var now = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
        var sops = registry.GetEffective(now, assetType: "SOP");
        var sop = Assert.Single(sops).Declaration;
        Assert.True(sop.SemanticSearchEnabled);
        var edge = Assert.Single(sop.Relationships);
        Assert.Equal("policy-client-data", edge.RelatedAssetId);
        Assert.Equal("implements", edge.RelationshipType);
        // MagCPA's plain citation case: no relationships, still retrievable.
        Assert.Empty(Assert.Single(registry.GetEffective(now, assetType: "PolicyDocument")).Declaration.Relationships);
    }

    [Fact]
    public void R10_investor_is_cleared_only_when_every_check_is_verified()
    {
        var profile = new InvestorProfile
        {
            TenantId = Tenant,
            UserIdentifier = "lp1@altera.example",
            KycStatus = InvestorCheckStatuses.Verified,
            AmlStatus = InvestorCheckStatuses.Verified,
            AccreditationStatus = InvestorCheckStatuses.Pending
        };

        Assert.False(profile.IsCleared); // one Pending check blocks clearance
        profile.AccreditationStatus = InvestorCheckStatuses.Verified;
        Assert.True(profile.IsCleared);
        profile.AmlStatus = InvestorCheckStatuses.Rejected;
        Assert.False(profile.IsCleared); // a rejection un-clears, permanently until re-decided
    }

    [Fact]
    public async Task R11_risk_type_tags_ride_the_same_score_mechanism()
    {
        var scores = new InMemoryScoreStore();
        await scores.AppendAsync(new Domain.Scoring.ScoreRecord
        {
            TenantId = Tenant, SubjectType = "ITAsset", SubjectId = "prod-sql-01",
            ScoreType = "PatchCompliance", Value = 0.82m,
            RiskType = "Cybersecurity", Provenance = "ExternalSystemVerified",
            CorrelationId = Guid.NewGuid()
        });
        await scores.AppendAsync(new Domain.Scoring.ScoreRecord
        {
            TenantId = Tenant, SubjectType = "Campaign", SubjectId = "spring-promo",
            ScoreType = "SentimentIndex", Value = 0.4m,
            RiskType = "Marketing", Provenance = "AIGenerated",
            CorrelationId = Guid.NewGuid()
        });

        // One mechanism, different open tags — never a per-tenant RiskRecord.
        Assert.Equal("Cybersecurity", scores.Items.Single(s => s.SubjectId == "prod-sql-01").RiskType);
        Assert.Equal("Marketing", scores.Items.Single(s => s.SubjectId == "spring-promo").RiskType);
    }

    [Fact]
    public async Task Deviations_are_direction_aware_and_zero_targets_mean_zero_tolerance()
    {
        var registries = new TenantRegistryProvider();
        var scores = new InMemoryScoreStore();
        var alerts = new InMemoryDeviationStore();
        var audit = new InMemoryAuditLog();
        var evaluator = new PerformanceEvaluationService(registries, scores, audit,
            new DeviationMonitoringService(alerts, scores, audit));

        registries.GetFor(Tenant).PerformanceFrameworks.Register(new PerformanceFrameworkDeclaration
        {
            FrameworkId = "direction-v1", Name = "Direction", Industry = "Any",
            Kpis =
            [
                new KPIDefinition { KpiId = "training-hours", Formula = "trainingHours", TriggerRecordType = "Sheet", SubjectField = "who", TargetValue = 4, Direction = "higher" },
                new KPIDefinition { KpiId = "shrinkage", Formula = "shrinkage", TriggerRecordType = "Sheet", SubjectField = "who", TargetValue = 500, Direction = "lower" },
                new KPIDefinition { KpiId = "delay-days", Formula = "delayDays", TriggerRecordType = "Sheet", SubjectField = "who", TargetValue = 0, Direction = "lower" }
            ]
        }, "p", "1.0.0");

        await evaluator.EvaluateAsync(new KernelEvent(Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "Sheet", ["who"] = "unit-1",
                ["trainingHours"] = "8",    // ABOVE a higher-is-better target: favorable, no alert
                ["shrinkage"] = "150",      // far BELOW a lower-is-better target: favorable, no alert
                ["delayDays"] = "12"        // above a ZERO lower-is-better target: Critical breach
            }, DateTimeOffset.UtcNow));

        // The MagDynamics/JJ Fish false alarms are gone...
        Assert.DoesNotContain(alerts.Items, a => a.EntityReference == "training-hours@unit-1");
        Assert.DoesNotContain(alerts.Items, a => a.EntityReference == "shrinkage@unit-1");
        // ...and the Rosetta silence is fixed: zero target + direction = zero tolerance.
        var slip = Assert.Single(alerts.Items, a => a.EntityReference == "delay-days@unit-1");
        Assert.Equal("Critical", slip.Severity);
        Assert.Equal(12m, slip.ActualValue);

        // Unfavorable relative breaches still alert exactly as before.
        await evaluator.EvaluateAsync(new KernelEvent(Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "Sheet", ["who"] = "unit-2",
                ["trainingHours"] = "1", ["shrinkage"] = "900", ["delayDays"] = "0"
            }, DateTimeOffset.UtcNow));
        Assert.Contains(alerts.Items, a => a.EntityReference == "training-hours@unit-2"); // 1 vs 4, unfavorable
        Assert.Contains(alerts.Items, a => a.EntityReference == "shrinkage@unit-2");      // 900 vs 500, unfavorable
        Assert.DoesNotContain(alerts.Items, a => a.EntityReference == "delay-days@unit-2"); // on target
    }

    [Fact]
    public void R12_connectors_default_to_tenant_owned_and_accept_public_external()
    {
        var owned = new ConnectorDefinition(
            "chase-feed", "Chase", "Banking", ArrivalModes.File, "bank-mt940-text",
            0.9m, "ExternalSystemVerified", "Active");
        Assert.Equal(SourceOwnerships.TenantOwned, owned.SourceOwnership); // default preserves all existing connectors

        var monitoring = new ConnectorDefinition(
            "review-monitor", "Public review monitoring (Oxygen Spa)", "Communications",
            ArrivalModes.Pull, "review-feed-json", 0.5m, "AIGenerated", "Active",
            SourceOwnerships.PublicExternal);
        Assert.Equal(SourceOwnerships.PublicExternal, monitoring.SourceOwnership);
    }
}
