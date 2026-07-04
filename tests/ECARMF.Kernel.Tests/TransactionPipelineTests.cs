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
/// Full pipeline test using the shipped Treasury Controls v1 sample package:
/// transaction intake -> TransactionReceived event -> rule evaluation ->
/// explainable outcome, with the audit trail written along the way — all
/// tenant-scoped.
/// </summary>
public class TransactionPipelineTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryTransactionStore _transactionStore = new();
    private readonly InMemoryOutcomeStore _outcomeStore = new();
    private readonly InMemoryScoreStore _scoreStore = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();

    private static KnowledgePackageManifest LoadSampleManifest()
    {
        // Walk up from the test bin directory to the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "treasury-controls-v1.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", "treasury-controls-v1.json"));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private async Task ActivateSamplePackageAsync(string tenantId = Tenant)
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var loadResult = await loader.LoadAsync(tenantId, LoadSampleManifest());
        Assert.True(loadResult.Success, string.Join("; ", loadResult.Errors));
        var activateResult = await loader.ActivateAsync(tenantId, "ecarmf.treasury-controls", "1.0.0");
        Assert.True(activateResult.Success, string.Join("; ", activateResult.Errors));
    }

    private async Task<(TransactionReceipt Receipt, ProcessingResult Result)> SubmitAndProcessAsync(
        string transactionType, string amount, string tenantId = Tenant)
    {
        var intake = new TransactionIntakeService(_transactionStore, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            tenantId,
            transactionType,
            "treasurer@example.com",
            new Dictionary<string, string> { ["ventureId"] = "V-001", ["amount"] = amount }));

        // Drain exactly one event from the bus and process it, standing in
        // for the hosted service loop.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());

        var processor = new EventProcessor(_registries, _outcomeStore, _scoreStore, _bus, _audit);
        var result = await processor.ProcessAsync(enumerator.Current);
        return (receipt, result);
    }

    [Fact]
    public void Sample_manifest_deserializes_and_validates()
    {
        var manifest = LoadSampleManifest();

        Assert.Equal("ecarmf.treasury-controls", manifest.PackageId);
        Assert.Empty(ManifestValidator.Validate(manifest, new EventRegistry()));
    }

    [Fact]
    public async Task Large_withdrawal_is_flagged_by_treasury_rule_with_provenance()
    {
        await ActivateSamplePackageAsync();

        var (receipt, result) = await SubmitAndProcessAsync("withdrawal", "60000");

        Assert.True(receipt.EventPublished);
        Assert.NotNull(result.Outcome);
        Assert.Equal("Flagged", result.Outcome!.Outcome);
        Assert.Equal("TREASURY-R-001", result.Outcome.RuleId);
        Assert.Equal("ecarmf.treasury-controls", result.Outcome.PackageId);
        Assert.Equal("1.0.0", result.Outcome.PackageVersion);
        Assert.Equal(Tenant, result.Outcome.TenantId);
        Assert.Contains("60000", result.Outcome.Reason);
        Assert.Contains("V-001", result.Outcome.Reason);

        // Immutable stores hold the transaction and the outcome.
        Assert.Equal(receipt.TransactionId, _transactionStore.Items.Single().TransactionId);
        Assert.Equal(receipt.TransactionId, _outcomeStore.Items.Single().TransactionId);
    }

    [Fact]
    public async Task Small_withdrawal_is_approved_by_default_policy()
    {
        await ActivateSamplePackageAsync();

        var (_, result) = await SubmitAndProcessAsync("withdrawal", "1200");

        Assert.NotNull(result.Outcome);
        Assert.Equal("Approved", result.Outcome!.Outcome);
        Assert.Null(result.Outcome.RuleId);
        Assert.Contains("default", result.Outcome.Reason, StringComparison.OrdinalIgnoreCase);
        // The rule was still evaluated (and audited) even though it did not match.
        var evaluation = Assert.Single(result.Evaluations);
        Assert.False(evaluation.Matched);
    }

    [Fact]
    public async Task Non_withdrawal_transaction_is_approved_by_default_policy()
    {
        await ActivateSamplePackageAsync();

        var (_, result) = await SubmitAndProcessAsync("deposit", "500000");

        Assert.Equal("Approved", result.Outcome!.Outcome);
    }

    [Fact]
    public async Task Flagged_record_publishes_Flagged_follow_up_event()
    {
        await ActivateSamplePackageAsync();

        await SubmitAndProcessAsync("withdrawal", "60000");

        // The follow-up event name is the outcome string itself.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("Flagged", enumerator.Current.EventName);
        Assert.Equal("Flagged", enumerator.Current.Payload["outcome"]);
        Assert.Equal(Tenant, enumerator.Current.TenantId);
    }

    [Fact]
    public async Task Audit_trail_covers_receipt_evaluation_and_outcome()
    {
        await ActivateSamplePackageAsync();

        var (receipt, _) = await SubmitAndProcessAsync("withdrawal", "60000");
        var trail = await _audit.GetByCorrelationAsync(Tenant, receipt.TransactionId);

        Assert.Equal(
            [
                AuditCategories.RecordReceived,
                AuditCategories.EventPublished,
                AuditCategories.RuleEvaluated,
                AuditCategories.OutcomeRecorded
            ],
            trail.Select(t => t.Category).ToArray());
    }

    [Fact]
    public async Task Without_active_package_transaction_is_persisted_but_not_processed()
    {
        var intake = new TransactionIntakeService(_transactionStore, _bus, _registries, _audit);

        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            Tenant, "withdrawal", "treasurer@example.com",
            new Dictionary<string, string> { ["amount"] = "60000" }));

        Assert.False(receipt.EventPublished);
        Assert.NotNull(receipt.Note);
        Assert.Single(_transactionStore.Items);
    }

    [Fact]
    public async Task Package_active_for_one_tenant_does_not_process_another_tenants_transactions()
    {
        // Treasury controls are active for tenant-a only.
        await ActivateSamplePackageAsync("tenant-a");

        var intake = new TransactionIntakeService(_transactionStore, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            "tenant-b", "withdrawal", "treasurer@other-client.com",
            new Dictionary<string, string> { ["ventureId"] = "V-9", ["amount"] = "999999" }));

        // Tenant B has no active package: persisted, never processed —
        // tenant A's controls must not leak into tenant B's treasury.
        Assert.False(receipt.EventPublished);
        Assert.NotNull(receipt.Note);

        // And tenant B's audit trail is invisible to tenant A.
        var tenantATrail = await _audit.GetByCorrelationAsync("tenant-a", receipt.TransactionId);
        Assert.Empty(tenantATrail);
        var tenantBTrail = await _audit.GetByCorrelationAsync("tenant-b", receipt.TransactionId);
        Assert.Single(tenantBTrail);
    }
}
