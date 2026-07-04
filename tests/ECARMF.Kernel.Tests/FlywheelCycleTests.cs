using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// Full flywheel cycle using the shipped sample packages: collect an
/// opportunity -> validate data quality -> score -> decide -> track the
/// decision event -> learn (trust / control-effectiveness scores) — and
/// prove the entire cycle is reconstructable from one correlation id.
/// </summary>
public class FlywheelCycleTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryTransactionStore _recordStore = new();
    private readonly InMemoryOutcomeStore _outcomeStore = new();
    private readonly InMemoryScoreStore _scoreStore = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();

    private static KnowledgePackageManifest LoadManifest(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", fileName)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", fileName));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private async Task ActivateBothPackagesAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);

        var treasury = await loader.LoadAsync(Tenant, LoadManifest("treasury-controls-v1.json"));
        Assert.True(treasury.Success, string.Join("; ", treasury.Errors));
        var treasuryActive = await loader.ActivateAsync(Tenant, "ecarmf.treasury-controls", "1.1.0");
        Assert.True(treasuryActive.Success, string.Join("; ", treasuryActive.Errors));

        var flywheel = await loader.LoadAsync(Tenant, LoadManifest("flywheel-opportunity-evaluation-v1.json"));
        Assert.True(flywheel.Success, string.Join("; ", flywheel.Errors));
        var flywheelActive = await loader.ActivateAsync(Tenant, "ecarmf.flywheel-opportunity-evaluation", "1.0.0");
        Assert.True(flywheelActive.Success, string.Join("; ", flywheelActive.Errors));
    }

    /// <summary>Submits an Opportunity record and drains/processes the whole
    /// event chain (intake event plus any follow-up decision events).</summary>
    private async Task<Guid> SubmitOpportunityAndProcessAllAsync(Dictionary<string, string> payload)
    {
        var intake = new TransactionIntakeService(_recordStore, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            Tenant, "Opportunity", "scout@example.com", payload));
        Assert.True(receipt.EventPublished);

        var processor = new EventProcessor(_registries, _outcomeStore, _scoreStore, _bus, _audit);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        // Intake event, then any follow-up events the processing published.
        var pending = 1;
        while (pending > 0)
        {
            Assert.True(await enumerator.MoveNextAsync());
            pending--;
            var result = await processor.ProcessAsync(enumerator.Current);
            if (result.Outcome is not null
                && string.Equals(result.EventName, KernelEventNames.RecordReceived, StringComparison.OrdinalIgnoreCase)
                && _registries.GetFor(Tenant).Events.IsDeclared(result.Outcome.Outcome))
            {
                pending++;
            }
        }

        return receipt.TransactionId;
    }

    private static Dictionary<string, string> QualifiedOpportunity(string id = "OPP-1") => new()
    {
        ["opportunityId"] = id,
        ["sourceType"] = "broker-network",
        ["reliabilityRating"] = "0.9",
        ["estimatedValue"] = "1200000",
        ["riskRating"] = "0.3",
        ["complianceRating"] = "0.95",
        ["readinessRating"] = "0.8"
    };

    [Fact]
    public void Flywheel_manifest_deserializes_and_validates_against_treasury_events()
    {
        var manifest = LoadManifest("flywheel-opportunity-evaluation-v1.json");
        Assert.Equal("ecarmf.flywheel-opportunity-evaluation", manifest.PackageId);

        // Rules trigger on RecordReceived, declared by the treasury dependency.
        var events = new EventRegistry();
        events.Register(new EventDeclaration { EventName = "RecordReceived" }, "ecarmf.treasury-controls", "1.0.0");
        Assert.Empty(ManifestValidator.Validate(manifest, events));
    }

    [Fact]
    public async Task Qualified_opportunity_is_scored_accepted_and_learns_source_trust()
    {
        await ActivateBothPackagesAsync();

        var correlationId = await SubmitOpportunityAndProcessAllAsync(QualifiedOpportunity());

        // Validation + evaluation scores, all with provenance and correlation.
        var history = await _scoreStore.GetHistoryAsync(Tenant, "Opportunity", "OPP-1");
        var byType = history.ToDictionary(s => s.ScoreType);
        Assert.Equal(0.9m, byType["DataConfidence"].Value);
        Assert.Equal(0.3m, byType["RiskScore"].Value);
        Assert.Equal(1200000m, byType["Valuation"].Value);
        Assert.Equal(0.95m, byType["Compliance"].Value);
        Assert.Equal(0.8m, byType["AssetReadiness"].Value);
        Assert.All(history, s => Assert.Equal(correlationId, s.CorrelationId));
        Assert.All(history, s => Assert.Equal("ecarmf.flywheel-opportunity-evaluation", s.PackageId));

        // Decision: Accept by FLY-R-030 (Escalate did not apply: value under limit).
        var outcome = _outcomeStore.Items.Single(o => o.EventName == KernelEventNames.RecordReceived);
        Assert.Equal("Accept", outcome.Outcome);
        Assert.Equal("FLY-R-030", outcome.RuleId);
        Assert.Contains("OPP-1", outcome.Reason);

        // Learning: processing the Accept follow-up recorded source trust.
        var trust = await _scoreStore.GetHistoryAsync(Tenant, "OpportunitySource", "broker-network");
        var trustScore = Assert.Single(trust);
        Assert.Equal("Trust", trustScore.ScoreType);
        Assert.Equal(0.9m, trustScore.Value);
        Assert.Equal(correlationId, trustScore.CorrelationId);
    }

    [Fact]
    public async Task Low_confidence_opportunity_is_held_and_control_effectiveness_is_learned()
    {
        await ActivateBothPackagesAsync();

        var payload = QualifiedOpportunity("OPP-2");
        payload["reliabilityRating"] = "0.3";
        var correlationId = await SubmitOpportunityAndProcessAllAsync(payload);

        var outcome = _outcomeStore.Items.Single(o => o.EventName == KernelEventNames.RecordReceived);
        Assert.Equal("Hold", outcome.Outcome);
        Assert.Equal("FLY-R-032", outcome.RuleId);

        // The Hold follow-up taught the kernel the control worked.
        var control = _scoreStore.Items.Single(s => s.ScoreType == "ControlEffectiveness");
        Assert.Equal(1m, control.Value);
        Assert.Equal(correlationId, control.CorrelationId);
    }

    [Fact]
    public async Task Outsized_opportunity_escalates_before_acceptance()
    {
        await ActivateBothPackagesAsync();

        var payload = QualifiedOpportunity("OPP-3");
        payload["estimatedValue"] = "6000000";
        await SubmitOpportunityAndProcessAllAsync(payload);

        var outcome = _outcomeStore.Items.Single(o => o.EventName == KernelEventNames.RecordReceived);
        Assert.Equal("Escalate", outcome.Outcome);
        Assert.Equal("FLY-R-025", outcome.RuleId);
    }

    [Fact]
    public async Task Whole_cycle_is_reconstructable_from_one_correlation_id()
    {
        await ActivateBothPackagesAsync();

        var correlationId = await SubmitOpportunityAndProcessAllAsync(QualifiedOpportunity("OPP-4"));
        var cycle = await _audit.GetByCorrelationAsync(Tenant, correlationId);

        // One audit trail covers the full flywheel: collect -> validate ->
        // score -> decide -> execute/track -> learn.
        var categories = cycle.Select(c => c.Category).ToList();
        Assert.Contains(AuditCategories.RecordReceived, categories);
        Assert.Contains(AuditCategories.EventPublished, categories);
        Assert.Contains(AuditCategories.RuleEvaluated, categories);
        Assert.Contains(AuditCategories.ScoreComputed, categories);
        Assert.Contains(AuditCategories.OutcomeRecorded, categories);

        // Validation, evaluation, and learning scores all audited in-cycle.
        var scoreEntries = cycle.Where(c => c.Category == AuditCategories.ScoreComputed).ToList();
        Assert.Contains(scoreEntries, s => s.Detail["scoreType"] == "DataConfidence");
        Assert.Contains(scoreEntries, s => s.Detail["scoreType"] == "AssetReadiness");
        Assert.Contains(scoreEntries, s => s.Detail["scoreType"] == "Trust");

        // Scoring rules fired before the deciding rule inside the same cycle,
        // and evaluation stopped at the decision (first outcome rule wins) —
        // rules after it, like TREASURY-R-001 at priority 100, never ran.
        var evaluatedRules = cycle
            .Where(c => c.Category == AuditCategories.RuleEvaluated)
            .Select(c => c.Detail["ruleId"])
            .ToList();
        // FLY-R-040 is the learning rule evaluated on the Accept follow-up.
        Assert.Equal(["FLY-R-010", "FLY-R-020", "FLY-R-025", "FLY-R-030", "FLY-R-040"], evaluatedRules);
    }

    [Fact]
    public async Task Treasury_withdrawals_still_work_alongside_the_flywheel_package()
    {
        await ActivateBothPackagesAsync();

        var intake = new TransactionIntakeService(_recordStore, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            Tenant, "withdrawal", "treasurer@example.com",
            new Dictionary<string, string> { ["ventureId"] = "V-001", ["amount"] = "60000" }));

        var processor = new EventProcessor(_registries, _outcomeStore, _scoreStore, _bus, _audit);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());
        var result = await processor.ProcessAsync(enumerator.Current);

        Assert.Equal("Flagged", result.Outcome!.Outcome);
        Assert.Equal("TREASURY-R-001", result.Outcome.RuleId);
        // Flywheel scoring rules did not fire for a non-Opportunity record.
        Assert.Empty(_scoreStore.Items);
        _ = receipt;
    }
}
