using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Agents;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Transactions;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryAgentInteractionStore : IAgentInteractionStore
{
    public List<AgentInteraction> Items { get; } = [];
    public Task AddAsync(AgentInteraction i, CancellationToken ct = default) { Items.Add(i); return Task.CompletedTask; }
    public Task<AgentInteraction?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.TenantId == tenantId && i.Id == id));
    public Task UpdateFeedbackAsync(AgentInteraction i, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<AgentInteraction>> GetRecentAsync(string tenantId, string? agentId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentInteraction>>(Items.Where(i => i.TenantId == tenantId).ToList());
}

public class InMemoryUserStore : IUserStore
{
    public List<User> Items { get; } = [];
    public Task<User?> GetByIdentifierAsync(string tenantId, string identifier, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(u => u.TenantId == tenantId
            && string.Equals(u.Identifier, identifier, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<User>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>(Items.Where(u => u.TenantId == tenantId).ToList());
    public Task EnsureSeedUsersAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<User?> GetByAccessKeyHashAsync(string hash, CancellationToken ct = default) => Task.FromResult<User?>(null);
    public Task CreateUserAsync(User user, string? accessKeyHash, CancellationToken ct = default)
    { Items.Add(user); return Task.CompletedTask; }
    public Task SetAccessKeyHashAsync(string tenantId, string identifier, string hash, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetStatusAsync(string tenantId, string identifier, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetRolesAsync(string tenantId, string identifier, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var user = Items.FirstOrDefault(u => u.TenantId == tenantId && u.Identifier == identifier);
        if (user is not null) user.Roles = [.. roles];
        return Task.CompletedTask;
    }
}

/// <summary>Declared AI agents: packages ship domain specialists (the IRS
/// guide, a compliance guide, ...) the same way they ship rules; the kernel
/// supplies guardrails, tenant credentials, identity, audit, and trust.</summary>
public class AgentConsultTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TenantRegistryProvider _registries = new();
    private readonly FakeLanguageModelClient _llm = new();
    private readonly InMemoryAgentInteractionStore _interactions = new();
    private readonly InMemoryUserStore _users = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryDeviationStore _deviations = new();
    private readonly InMemoryTransactionStore _records = new();

    private AgentConsultService CreateService() => new(
        _registries, new FakeLanguageModelProvider(_llm), _interactions, _users,
        new AILearningFeedbackService(_scores, _audit), _audit,
        _scores, _deviations, new InMemoryBenchmarkStore(), new InMemoryTaskStore(),
        new InMemoryCapitalFlowStore(), new InMemoryDocumentLibrary(), _records);

    private void RegisterAgent(params string[] contextSources) =>
        _registries.GetFor(Tenant).Agents.Register(new AgentDeclaration
        {
            AgentId = "irs-guide",
            Name = "IRS Corporate Tax Guide",
            Persona = "You are the IRS Corporate Tax Guide. The published flat corporate rate is 21% for tax years 2018+.",
            ContextSources = [.. contextSources]
        }, "ecarmf.irs-corporate-tax-rates", "1.1.0");

    [Fact]
    public void IRS_package_declares_the_guide_agent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "irs-corporate-tax-rates-v1.json")))
        {
            directory = directory.Parent;
        }
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", "irs-corporate-tax-rates-v1.json"));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions)!;

        var agent = Assert.Single(manifest.Agents);
        Assert.Equal("irs-guide", agent.AgentId);
        Assert.Contains("21%", agent.Persona);
        Assert.Contains("records:CorporateTaxReturn", agent.ContextSources);

        // The package's rules trigger on RecordReceived, declared by its
        // treasury dependency — mirror that in the validator's registry.
        var events = new EventRegistry();
        events.Register(new EventDeclaration { EventName = "RecordReceived" }, "ecarmf.treasury-controls", "1.2.0");
        Assert.Empty(ManifestValidator.Validate(manifest, events));
    }

    [Fact]
    public void Validator_rejects_agents_without_persona_or_with_bad_sources()
    {
        var manifest = new KnowledgePackageManifest
        {
            PackageId = "p", Name = "p", PackageVersion = "1.0.0",
            Agents = [new AgentDeclaration { AgentId = "a1", Name = "A1", ContextSources = ["everything"] }]
        };

        var errors = ManifestValidator.Validate(manifest, new EventRegistry());

        Assert.Contains(errors, e => e.Contains("no Persona"));
        Assert.Contains(errors, e => e.Contains("invalid context source 'everything'"));
    }

    [Fact]
    public async Task Consulting_an_agent_grounds_it_guards_it_and_audits_under_its_own_identity()
    {
        RegisterAgent("records:CorporateTaxReturn", "deviations");
        await _records.AppendAsync(new Transaction
        {
            TenantId = Tenant, TransactionType = "CorporateTaxReturn", SubmittedBy = "cpa@example.com",
            Payload = new Dictionary<string, string> { ["returnId"] = "RTN-1", ["taxableIncome"] = "1000000", ["reportedTax"] = "120000" }
        });
        _llm.IsConfigured = true;
        _llm.Response = "RTN-1 shows a 12% effective rate against the published 21%...";

        var service = CreateService();
        var (success, error, interaction) = await service.AskAsync(
            Tenant, "irs-guide", "Why was RTN-1 flagged?", "owner@platform");

        Assert.True(success, error);
        // Kernel guardrails wrap the package persona.
        Assert.Contains("advisory agent", _llm.LastSystemPrompt);
        Assert.Contains("you advise only", _llm.LastSystemPrompt);
        Assert.Contains("IRS Corporate Tax Guide", _llm.LastSystemPrompt);
        // Only declared sources appear in the grounding context.
        Assert.Contains("RTN-1", _llm.LastUserPrompt);
        Assert.Contains("openDeviations", _llm.LastUserPrompt);
        Assert.DoesNotContain("pendingAllocations", _llm.LastUserPrompt);

        Assert.Equal("agent:irs-guide:fake-model", interaction!.ModelReference);
        Assert.Equal("ecarmf.irs-corporate-tax-rates", interaction.PackageId);

        // The agent acts under its own provisioned identity.
        var identity = Assert.Single(_users.Items);
        Assert.Equal("system:agent:irs-guide", identity.Identifier);
        Assert.True(identity.IsSystemActor);
        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.AgentConsulted);
        Assert.Equal("system:agent:irs-guide", audit.Actor);
    }

    [Fact]
    public async Task Unknown_agent_and_missing_credential_fail_clearly()
    {
        var service = CreateService();

        var (unknownOk, unknownError, _) = await service.AskAsync(Tenant, "no-such-agent", "hi", "owner@platform");
        Assert.False(unknownOk);
        Assert.Contains("No active package", unknownError);

        RegisterAgent("scores");
        _llm.IsConfigured = false;
        var (noKeyOk, noKeyError, _) = await service.AskAsync(Tenant, "irs-guide", "hi", "owner@platform");
        Assert.False(noKeyOk);
        Assert.Contains("Anthropic API key", noKeyError);
        Assert.Empty(_interactions.Items);
    }

    [Fact]
    public async Task Human_feedback_feeds_the_agents_own_trust_history()
    {
        RegisterAgent("scores");
        _llm.IsConfigured = true;
        _llm.Response = "Grounded answer.";
        var service = CreateService();
        var (_, _, interaction) = await service.AskAsync(Tenant, "irs-guide", "question", "owner@platform");

        var human = new User { Identifier = "owner@platform", IsSystemActor = false, Roles = [RoleCatalog.ExecutiveOwner] };
        var (success, error, _) = await service.RecordFeedbackAsync(Tenant, interaction!.Id, true, human);

        Assert.True(success, error);
        var accuracy = Assert.Single(_scores.Items, s => s.ScoreType == "ModelAccuracy");
        Assert.Equal("agent:irs-guide:fake-model", accuracy.SubjectId);
        Assert.Equal(1m, accuracy.Value);

        var ai = new User { Identifier = "system:flywheel", IsSystemActor = true };
        var (aiOk, aiError, _) = await service.RecordFeedbackAsync(Tenant, interaction.Id, true, ai);
        Assert.False(aiOk);
        Assert.Contains("AI/system actor", aiError);
    }
}
