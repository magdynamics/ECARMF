using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Performance;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>KPI formula tests for all four frameworks against known
/// input/output pairs, plus the live Build Chain evaluation path.</summary>
public class PerformanceFrameworkTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();

    private static KnowledgePackageManifest LoadManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "performance-frameworks-v1.json")))
        {
            directory = directory.Parent;
        }
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", "performance-frameworks-v1.json"));
        return JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions)!;
    }

    private static PerformanceFrameworkDeclaration Framework(string id) =>
        LoadManifest().PerformanceFrameworks.Single(f => f.FrameworkId == id);

    private static decimal Eval(PerformanceFrameworkDeclaration framework, string kpiId, Dictionary<string, string> inputs)
    {
        var kpi = framework.Kpis.Single(k => k.KpiId == kpiId);
        Assert.True(KpiFormulaEvaluator.TryEvaluate(kpi.Formula, inputs, out var value), kpi.Formula);
        return value;
    }

    [Fact]
    public void Frameworks_manifest_validates()
    {
        var manifest = LoadManifest();
        Assert.Equal(4, manifest.PerformanceFrameworks.Count);
        Assert.Empty(ManifestValidator.Validate(manifest, new EventRegistry()));
    }

    [Fact]
    public void BuildChain_formulas_compute_known_values()
    {
        var framework = Framework("buildchain-operational-v1");
        Assert.Equal(0.8m, Eval(framework, "on-time-completion",
            new() { ["completedOnTime"] = "8", ["totalCompleted"] = "10" }));
        Assert.Equal(0.1m, Eval(framework, "cost-variance",
            new() { ["actualCost"] = "110000", ["plannedCost"] = "100000" }));
        Assert.Equal(0.75m, Eval(framework, "equipment-utilization",
            new() { ["runtimeHours"] = "150", ["availableHours"] = "200" }));
        Assert.Equal(0.0005m, Eval(framework, "safety-incident-rate",
            new() { ["incidents"] = "1", ["laborHours"] = "2000" }));
    }

    [Fact]
    public void Restaurant_realestate_renewable_formulas_compute_known_values()
    {
        Assert.Equal(4.5m, Eval(Framework("restaurant-operations-v1"), "table-turnover",
            new() { ["partiesServed"] = "90", ["tables"] = "20" }));
        Assert.Equal(0.28m, Eval(Framework("restaurant-operations-v1"), "food-cost-pct",
            new() { ["foodCost"] = "28000", ["revenue"] = "100000" }));

        Assert.Equal(0.95m, Eval(Framework("realestate-portfolio-v1"), "occupancy-rate",
            new() { ["occupiedUnits"] = "38", ["totalUnits"] = "40" }));
        Assert.Equal(0.6m, Eval(Framework("realestate-portfolio-v1"), "noi-margin",
            new() { ["revenue"] = "500000", ["operatingExpenses"] = "200000" }));

        Assert.Equal(0.35m, Eval(Framework("renewable-energy-v1"), "capacity-factor",
            new() { ["actualMwh"] = "2520", ["nameplateMw"] = "10", ["periodHours"] = "720" }));
        Assert.Equal(12.5m, Eval(Framework("renewable-energy-v1"), "om-cost-per-mwh",
            new() { ["omCost"] = "31500", ["actualMwh"] = "2520" }));
    }

    [Fact]
    public void Division_by_zero_or_missing_fields_fails_cleanly()
    {
        Assert.False(KpiFormulaEvaluator.TryEvaluate("a / b", new Dictionary<string, string> { ["a"] = "1", ["b"] = "0" }, out _));
        Assert.False(KpiFormulaEvaluator.TryEvaluate("a / b", new Dictionary<string, string> { ["a"] = "1" }, out _));
    }

    [Fact]
    public async Task Live_buildchain_evaluation_emits_kpi_variance_and_okr_scores()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest();
        Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
        Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);

        var evaluator = new PerformanceEvaluationService(_registries, _scores, _audit);
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["recordType"] = "OperationalEvent",
            ["siteId"] = "Bridgeview-Lot7",
            ["completedOnTime"] = "9",
            ["totalCompleted"] = "10",
            ["actualCost"] = "104000",
            ["plannedCost"] = "100000",
            ["runtimeHours"] = "160",
            ["availableHours"] = "200",
            ["incidents"] = "0",
            ["laborHours"] = "2000"
        };

        await evaluator.EvaluateAsync(new KernelEvent(
            Tenant, KernelEventNames.RecordReceived, Guid.NewGuid(), payload, DateTimeOffset.UtcNow));

        // Four KPIActuals + four KPIVariances + one OKRAttainment.
        Assert.Equal(4, _scores.Items.Count(s => s.ScoreType == "KPIActual"));
        Assert.Equal(4, _scores.Items.Count(s => s.ScoreType == "KPIVariance"));
        var okr = _scores.Items.Single(s => s.ScoreType == "OKRAttainment");
        Assert.Equal("buildchain-operational-excellence@Bridgeview-Lot7", okr.SubjectId);
        // on-time 0.9/0.9 = 1.0 capped; utilization 0.8/0.75 capped at 1 -> avg 1.
        Assert.Equal(1m, okr.Value);

        var utilization = _scores.Items.Single(s => s.ScoreType == "KPIActual" && s.SubjectId.StartsWith("equipment-utilization"));
        Assert.Equal(0.8m, utilization.Value);
        Assert.Equal("ecarmf.performance-frameworks", utilization.PackageId);
    }

    [Fact]
    public void Framework_recommender_matches_industry_classification()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest();
        loader.LoadAsync(Tenant, manifest).GetAwaiter().GetResult();
        loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion).GetAwaiter().GetResult();

        var recommender = new PerformanceEvaluationService(_registries, _scores, _audit);
        var matches = recommender.Recommend(Tenant, "Construction");

        var match = Assert.Single(matches);
        Assert.Equal("buildchain-operational-v1", match.Declaration.FrameworkId);
    }
}
