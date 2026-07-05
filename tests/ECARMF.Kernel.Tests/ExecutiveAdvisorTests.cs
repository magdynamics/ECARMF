using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Domain.Advisor;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryDeviationStore : IDeviationStore
{
    public List<DeviationAlert> Items { get; } = [];
    public Task AddAsync(DeviationAlert alert, CancellationToken ct = default) { Items.Add(alert); return Task.CompletedTask; }
    public Task<DeviationAlert?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(d => d.TenantId == tenantId && d.Id == id));
    public Task UpdateAsync(DeviationAlert alert, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<DeviationAlert>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviationAlert>>(Items.Where(d => d.TenantId == tenantId).Take(limit).ToList());
}

public class InMemoryAdvisorStore : IAdvisorStore
{
    public List<AdvisorBrief> Items { get; } = [];
    public Task AddAsync(AdvisorBrief brief, CancellationToken ct = default) { Items.Add(brief); return Task.CompletedTask; }
    public Task<AdvisorBrief?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(b => b.TenantId == tenantId && b.Id == id));
    public Task UpdateFeedbackAsync(AdvisorBrief brief, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<AdvisorBrief>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdvisorBrief>>(Items.Where(b => b.TenantId == tenantId).Take(limit).ToList());
}

/// <summary>Per-tenant provider that hands every tenant the same scripted
/// client; records which tenant asked, so tests can assert isolation.</summary>
public class FakeLanguageModelProvider : ILanguageModelProvider
{
    private readonly ILanguageModelClient _client;
    public List<string> RequestedTenants { get; } = [];

    public FakeLanguageModelProvider(ILanguageModelClient client) => _client = client;

    public Task<ILanguageModelClient> GetForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        RequestedTenants.Add(tenantId);
        return Task.FromResult(_client);
    }
}

/// <summary>Scripted language model backend for tests.</summary>
public class FakeLanguageModelClient : ILanguageModelClient
{
    public bool IsConfigured { get; set; }
    public string ModelReference => "fake-model";
    public string? Response { get; set; }
    public Exception? Throws { get; set; }
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        if (Throws is not null) throw Throws;
        return Task.FromResult(Response ?? string.Empty);
    }
}

/// <summary>The Executive Advisor: reads operational state, produces an
/// audited AI-actor brief, and earns trust through human feedback.</summary>
public class ExecutiveAdvisorTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryDeviationStore _deviations = new();
    private readonly InMemoryAllocationStore _allocations = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryAdvisorStore _briefs = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly FakeLanguageModelClient _llm = new();

    private readonly FakeLanguageModelProvider _llmProvider;

    public ExecutiveAdvisorTests() => _llmProvider = new FakeLanguageModelProvider(_llm);

    private ExecutiveAdvisorService CreateAdvisor() => new(
        _scores, _deviations, _allocations, _tasks, _briefs, _llmProvider,
        new AILearningFeedbackService(_scores, _audit), _audit);

    private static User Human(string identifier = "owner@platform") => new()
    {
        Identifier = identifier, DisplayName = identifier, IsSystemActor = false,
        Roles = [RoleCatalog.ExecutiveOwner]
    };

    [Fact]
    public async Task Deterministic_brief_covers_deviations_allocations_and_tasks()
    {
        _deviations.Items.Add(new DeviationAlert
        {
            TenantId = Tenant, EntityReference = "occupancy@site-1", MetricType = "KPIActual",
            Severity = "Critical", VarianceMagnitude = -0.55m
        });
        _allocations.Items.Add(new AllocationRecommendation
        {
            TenantId = Tenant, TargetReference = "venture-9", RecommendedAmount = 2_000_000m,
            ConfidenceScore = 0.4m, Tier = AutonomyTier.Escalated, Status = "Pending"
        });

        var brief = await CreateAdvisor().GenerateBriefAsync(Tenant, "owner@platform");

        Assert.Equal(Provenance.AIGenerated, brief.Provenance);
        Assert.Equal(ExecutiveAdvisorService.DeterministicModelReference, brief.ModelReference);
        Assert.Contains("critical deviation", brief.ExecutiveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(brief.Recommendations, r => r.Priority == "High" && r.Recommendation.Contains("occupancy@site-1"));
        Assert.Contains(brief.Recommendations, r => r.Recommendation.Contains("venture-9"));
        Assert.Single(_briefs.Items);

        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.AdvisorBriefGenerated);
        Assert.Equal(ExecutiveAdvisorService.ActorIdentifier, audit.Actor);
        Assert.Equal(brief.CorrelationId, audit.CorrelationId);
    }

    [Fact]
    public async Task Empty_tenant_still_gets_an_actionable_brief()
    {
        var brief = await CreateAdvisor().GenerateBriefAsync(Tenant, "owner@platform");

        Assert.NotEmpty(brief.ExecutiveSummary);
        var recommendation = Assert.Single(brief.Recommendations);
        Assert.Contains("knowledge package", recommendation.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configured_model_composes_the_brief_and_is_credited_in_provenance()
    {
        _llm.IsConfigured = true;
        _llm.Response = """
            Here is the brief you asked for:
            {"title": "Q3 Posture", "executiveSummary": "Two risks dominate.",
             "recommendations": [{"recommendation": "Do X", "rationale": "Because Y", "priority": "High"}]}
            """;

        var brief = await CreateAdvisor().GenerateBriefAsync(Tenant, "owner@platform");

        Assert.Equal("Q3 Posture", brief.Title);
        Assert.Equal("Two risks dominate.", brief.ExecutiveSummary);
        Assert.Equal("advisor:fake-model", brief.ModelReference);
        // The backend was resolved for THIS tenant — credentials never cross tenants.
        Assert.Equal(Tenant, Assert.Single(_llmProvider.RequestedTenants));
        // The snapshot, not free text, is what the model reasons over.
        Assert.Contains("scoreAverages", _llm.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.AdvisorBriefGenerated);
        Assert.Equal("llm", audit.Detail["backend"]);
    }

    [Fact]
    public async Task Model_failure_falls_back_to_the_deterministic_composer()
    {
        _llm.IsConfigured = true;
        _llm.Throws = new InvalidOperationException("api unreachable");

        var brief = await CreateAdvisor().GenerateBriefAsync(Tenant, "owner@platform");

        Assert.Equal(ExecutiveAdvisorService.DeterministicModelReference, brief.ModelReference);
        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.AdvisorBriefGenerated);
        Assert.Equal("deterministic-fallback", audit.Detail["backend"]);
    }

    [Fact]
    public async Task Malformed_model_output_falls_back_instead_of_failing()
    {
        _llm.IsConfigured = true;
        _llm.Response = "I think you should diversify. (not JSON)";

        var brief = await CreateAdvisor().GenerateBriefAsync(Tenant, "owner@platform");

        Assert.Equal(ExecutiveAdvisorService.DeterministicModelReference, brief.ModelReference);
        Assert.NotEmpty(brief.Recommendations);
    }

    [Fact]
    public async Task Human_feedback_feeds_the_ModelAccuracy_trust_loop()
    {
        var advisor = CreateAdvisor();
        var brief = await advisor.GenerateBriefAsync(Tenant, "owner@platform");

        var (success, error, updated) = await advisor.RecordFeedbackAsync(Tenant, brief.Id, useful: false, Human());

        Assert.True(success, error);
        Assert.False(updated!.FeedbackUseful);
        var accuracy = Assert.Single(_scores.Items, s => s.ScoreType == "ModelAccuracy");
        Assert.Equal(0m, accuracy.Value);
        Assert.Equal(brief.ModelReference, accuracy.SubjectId);
        Assert.Equal(brief.CorrelationId, accuracy.CorrelationId);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.AdvisorFeedbackRecorded);
    }

    [Fact]
    public async Task System_actor_cannot_rate_briefs_and_feedback_is_not_revotable()
    {
        var advisor = CreateAdvisor();
        var brief = await advisor.GenerateBriefAsync(Tenant, "owner@platform");

        var ai = new User { Identifier = "system:flywheel", IsSystemActor = true, Roles = [RoleCatalog.AISystemActor] };
        var (aiSuccess, aiError, _) = await advisor.RecordFeedbackAsync(Tenant, brief.Id, true, ai);
        Assert.False(aiSuccess);
        Assert.Contains("AI/system actor", aiError);

        Assert.True((await advisor.RecordFeedbackAsync(Tenant, brief.Id, true, Human())).Success);
        var (second, secondError, _) = await advisor.RecordFeedbackAsync(Tenant, brief.Id, false, Human("admin@platform"));
        Assert.False(second);
        Assert.Contains("already", secondError);
    }
}
