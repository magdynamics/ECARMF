using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// Public reference data as a package: the IRS published corporate rate is
/// the KPI target, client returns come in as records, and the comparison —
/// effective rate vs the published 21% — runs through the same KPI/deviation
/// machinery as everything else.
/// </summary>
public class IrsRateComparisonTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryTransactionStore _records = new();
    private readonly InMemoryOutcomeStore _outcomes = new();
    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();
    private readonly InMemoryDeviationStore _alerts = new();

    private static KnowledgePackageManifest LoadManifest(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", fileName)))
        {
            directory = directory.Parent;
        }
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", fileName));
        return JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions)!;
    }

    private async Task ActivateAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        Assert.True((await loader.LoadAsync(Tenant, LoadManifest("treasury-controls-v1.json"))).Success);
        Assert.True((await loader.ActivateAsync(Tenant, "ecarmf.treasury-controls", "1.2.0")).Success);
        var load = await loader.LoadAsync(Tenant, LoadManifest("irs-corporate-tax-rates-v1.json"));
        Assert.True(load.Success, string.Join("; ", load.Errors));
        Assert.True((await loader.ActivateAsync(Tenant, "ecarmf.irs-corporate-tax-rates", "1.0.0")).Success);
    }

    private async Task<Guid> SubmitReturnAsync(Dictionary<string, string> payload)
    {
        var intake = new TransactionIntakeService(_records, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            Tenant, "CorporateTaxReturn", "cpa@example.com", payload));
        Assert.True(receipt.EventPublished);

        // Deviation monitoring wired into KPI evaluation, as in production DI.
        var deviations = new DeviationMonitoringService(_alerts, _scores, _audit);
        var performance = new ECARMF.Kernel.Application.Performance.PerformanceEvaluationService(
            _registries, _scores, _audit, deviations);
        var processor = new EventProcessor(_registries, _outcomes, _scores, _bus, _audit, performance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());
        await processor.ProcessAsync(enumerator.Current);
        return receipt.TransactionId;
    }

    [Fact]
    public async Task Return_paying_far_below_the_published_rate_raises_the_alarm()
    {
        await ActivateAsync();

        var id = await SubmitReturnAsync(new Dictionary<string, string>
        {
            ["returnId"] = "RTN-2025-001",
            ["entityName"] = "Acme Holdings Inc",
            ["taxYear"] = "2025",
            ["taxableIncome"] = "1000000",
            ["reportedTax"] = "120000" // 12% effective vs published 21%
        });

        var outcome = Assert.Single(await _outcomes.GetForTransactionsAsync(Tenant, [id]));
        Assert.Equal("Approved", outcome.Outcome);
        Assert.Equal("IRS-R-030", outcome.RuleId);

        var kpi = Assert.Single(_scores.Items, s => s.ScoreType == "KPIActual");
        Assert.Equal(0.12m, kpi.Value);
        Assert.Contains("effective-tax-rate", kpi.SubjectId);

        // |0.12 - 0.21| / 0.21 = 43% gap -> Critical deviation from the published rate.
        var alert = Assert.Single(_alerts.Items);
        Assert.Equal(0.12m, alert.ActualValue);
        Assert.Equal(0.21m, alert.ExpectedValue);
        Assert.Equal("Critical", alert.Severity);
        Assert.Equal("Target", alert.ExpectedValueSource);
    }

    [Fact]
    public async Task Return_at_the_published_rate_passes_quietly()
    {
        await ActivateAsync();

        await SubmitReturnAsync(new Dictionary<string, string>
        {
            ["returnId"] = "RTN-2025-002",
            ["entityName"] = "Conforming Corp",
            ["taxYear"] = "2025",
            ["taxableIncome"] = "1000000",
            ["reportedTax"] = "210000" // exactly 21%
        });

        Assert.Empty(_alerts.Items);
        var kpi = Assert.Single(_scores.Items, s => s.ScoreType == "KPIActual");
        Assert.Equal(0.21m, kpi.Value);
    }

    [Fact]
    public async Task PreTCJA_years_are_flagged_for_manual_review_not_miscompared()
    {
        await ActivateAsync();

        var id = await SubmitReturnAsync(new Dictionary<string, string>
        {
            ["returnId"] = "RTN-2016-001",
            ["entityName"] = "Legacy Corp",
            ["taxYear"] = "2016",
            ["taxableIncome"] = "500000",
            ["reportedTax"] = "170000"
        });

        var outcome = Assert.Single(await _outcomes.GetForTransactionsAsync(Tenant, [id]));
        Assert.Equal("Flagged", outcome.Outcome);
        Assert.Equal("IRS-R-020", outcome.RuleId);
        Assert.Contains("graduated", outcome.Reason);
    }
}
