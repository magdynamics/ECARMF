using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryCapitalFlowStore : ICapitalFlowStore
{
    public List<CapitalFlow> Items { get; } = [];

    public Task AddAsync(CapitalFlow recommendation, CancellationToken ct = default)
    {
        Items.Add(recommendation);
        return Task.CompletedTask;
    }

    public Task<CapitalFlow?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.TenantId == tenantId && a.Id == id));

    public Task UpdateDecisionAsync(CapitalFlow recommendation, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<CapitalFlow>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CapitalFlow>>(
            Items.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.CreatedAt).Take(limit).ToList());
}

/// <summary>Capital Intelligence: recommendation generation grounded in
/// ScoreRecords, three-tier autonomy, and the no-AI-self-approval rule.</summary>
public class CapitalAllocationTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryCapitalFlowStore _allocations = new();
    private readonly InMemoryAuditLog _audit = new();

    private CapitalAllocationEngine CreateEngine() => new(_scores, _allocations, _audit);

    private void SeedScores(string subjectId, decimal readiness, decimal valuation, decimal confidence)
    {
        _scores.Items.Add(new ScoreRecord { TenantId = Tenant, SubjectType = "Opportunity", SubjectId = subjectId, ScoreType = "AssetReadiness", Value = readiness });
        _scores.Items.Add(new ScoreRecord { TenantId = Tenant, SubjectType = "Opportunity", SubjectId = subjectId, ScoreType = "Valuation", Value = valuation });
        _scores.Items.Add(new ScoreRecord { TenantId = Tenant, SubjectType = "Opportunity", SubjectId = subjectId, ScoreType = "DataConfidence", Value = confidence });
    }

    private static User Human(string id = "owner@platform") => new() { Identifier = id, IsSystemActor = false, Roles = [RoleCatalog.ExecutiveOwner] };
    private static User Ai() => new() { Identifier = "system:flywheel", IsSystemActor = true, Roles = [RoleCatalog.AISystemActor] };

    [Fact]
    public async Task Recommends_highest_readiness_with_ranked_alternatives_and_reasoning()
    {
        SeedScores("OPP-A", 0.9m, 800_000m, 0.9m);
        SeedScores("OPP-B", 0.6m, 500_000m, 0.8m);
        var engine = CreateEngine();

        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");

        Assert.NotNull(recommendation);
        Assert.Equal("OPP-A", recommendation!.TargetReference);
        Assert.Equal(800_000m, recommendation.Amount);
        Assert.Equal(0.9m, recommendation.ConfidenceScore);
        Assert.NotEmpty(recommendation.Reasoning);
        Assert.NotEmpty(recommendation.Assumptions);
        Assert.NotEmpty(recommendation.RiskFactors);
        var alternative = Assert.Single(recommendation.AlternativesConsidered);
        Assert.Equal("OPP-B", alternative.TargetReference);
        Assert.Equal(3, recommendation.SupportingScoreRecordIds.Count);
        // 800k with 0.9 confidence: above autonomous limit, below escalation.
        Assert.Equal(AutonomyTier.RecommendOnly, recommendation.Tier);
        Assert.Equal("Pending", recommendation.Status);
    }

    [Fact]
    public async Task Small_high_confidence_allocation_is_autonomous()
    {
        SeedScores("OPP-SMALL", 0.95m, 20_000m, 0.9m);
        var engine = CreateEngine();

        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");

        Assert.Equal(AutonomyTier.Autonomous, recommendation!.Tier);
        Assert.Equal("AutoExecuted", recommendation.Status);
    }

    [Fact]
    public async Task High_value_or_low_confidence_escalates()
    {
        SeedScores("OPP-BIG", 0.9m, 5_000_000m, 0.9m);
        var engine = CreateEngine();

        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");

        Assert.Equal(AutonomyTier.Escalated, recommendation!.Tier);
        Assert.Equal("Pending", recommendation.Status);
    }

    [Fact]
    public async Task AI_system_actor_cannot_decide_an_escalated_recommendation()
    {
        SeedScores("OPP-BIG", 0.9m, 5_000_000m, 0.9m);
        var engine = CreateEngine();
        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");

        var (success, error, _) = await engine.DecideAsync(
            Tenant, recommendation!.Id, Ai(), new AllocationDecision("Approve", null, null));

        Assert.False(success);
        Assert.Contains("AI/system actor cannot decide", error);
    }

    [Fact]
    public async Task Human_can_approve_modify_or_reject()
    {
        SeedScores("OPP-A", 0.9m, 800_000m, 0.9m);
        var engine = CreateEngine();
        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");

        var (success, _, decided) = await engine.DecideAsync(
            Tenant, recommendation!.Id, Human(), new AllocationDecision("Modify", 600_000m, "cap at 600k this quarter"));

        Assert.True(success);
        Assert.Equal("Modified", decided!.Status);
        Assert.Equal(600_000m, decided.ModifiedAmount);
        Assert.Equal("owner@platform", decided.DecidedBy);
    }

    [Fact]
    public async Task A_recommendation_is_decided_once()
    {
        SeedScores("OPP-A", 0.9m, 800_000m, 0.9m);
        var engine = CreateEngine();
        var recommendation = await engine.GenerateAsync(Tenant, "system:flywheel");
        await engine.DecideAsync(Tenant, recommendation!.Id, Human(), new AllocationDecision("Approve", null, null));

        var (success, error, _) = await engine.DecideAsync(
            Tenant, recommendation.Id, Human("cfo@platform"), new AllocationDecision("Reject", null, null));

        Assert.False(success);
        Assert.Contains("already", error);
    }
}
