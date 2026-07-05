using ECARMF.Kernel.Application.Onboarding;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryTemplateStore : IOnboardingTemplateStore
{
    public Dictionary<string, OnboardingTemplate> Items { get; } = [];

    public Task<IReadOnlyList<OnboardingTemplate>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OnboardingTemplate>>(Items.Values.ToList());

    public Task<OnboardingTemplate?> GetAsync(string templateId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(templateId, out var t) ? t : null);

    public Task UpsertAsync(OnboardingTemplate template, CancellationToken ct = default)
    {
        Items[template.TemplateId] = template;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string templateId, CancellationToken ct = default)
    {
        Items.Remove(templateId);
        return Task.CompletedTask;
    }
}

public class RecordingPackageLoader : IPackageLoader
{
    private readonly IPackageStore _store;
    public List<string> Loaded { get; } = [];
    public List<string> Activated { get; } = [];

    public RecordingPackageLoader(IPackageStore store) => _store = store;

    public async Task<PackageOperationResult> LoadAsync(
        string tenantId, KnowledgePackageManifest manifest, CancellationToken ct = default)
    {
        Loaded.Add($"{tenantId}:{manifest.PackageId}@{manifest.PackageVersion}");
        await _store.AddAsync(tenantId, manifest, PackageLoadState.Staged, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Staged);
    }

    public Task<PackageOperationResult> ActivateAsync(
        string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        Activated.Add($"{tenantId}:{packageId}@{packageVersion}");
        return Task.FromResult(PackageOperationResult.Ok(PackageLoadState.Active));
    }

    public Task<PackageOperationResult> DeactivateAsync(
        string tenantId, string packageId, string packageVersion, CancellationToken ct = default) =>
        Task.FromResult(PackageOperationResult.Ok(PackageLoadState.Deactivated));

    public Task RehydrateActiveAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Configure one client of a kind well, capture it, and every
/// future client starts the same way — additively, idempotently, audited.</summary>
public class OnboardingTemplateTests
{
    private readonly InMemoryTemplateStore _templates = new();
    private readonly InMemoryPackageStore _packages = new();
    private readonly RecordingPackageLoader _loader;
    private readonly InMemoryBenchmarkStore _benchmarks = new();
    private readonly InMemoryRenewalStore _renewals = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly OnboardingTemplateService _service;

    public OnboardingTemplateTests()
    {
        _loader = new RecordingPackageLoader(_packages);
        _service = new OnboardingTemplateService(
            _templates, _packages, _loader, _benchmarks, _renewals, _audit);
    }

    private async Task SeedSourceTenantAsync()
    {
        await _packages.AddAsync("restaurant-one", new KnowledgePackageManifest
        {
            PackageId = "pos-rules",
            Name = "POS rules",
            PackageVersion = "1.0.0"
        }, PackageLoadState.Active, null);

        _benchmarks.Items.Add(new Benchmark
        {
            TenantId = "restaurant-one",
            Name = "GP% at or above 25%",
            Kind = "score",
            MetricType = "GPPercent",
            ExpectationOperator = ConditionOperator.GreaterOrEqual,
            ExpectedValue = 0.25m,
            Severity = "Warning",
            NotifyRole = "ExecutiveOwner",
            CreatedBy = "op"
        });

        _renewals.Items.Add(new RenewalCommitment
        {
            TenantId = "restaurant-one",
            Name = "Food service license",
            Category = RenewalCategories.License,
            DueDate = DateTimeOffset.UtcNow.AddDays(200),
            RecurrenceMonths = 12,
            LeadTimeDays = new[] { 90, 30, 7 },
            NotifyRole = "ExecutiveOwner",
            CreatedBy = "op"
        });
    }

    [Fact]
    public async Task Capture_snapshots_packages_benchmarks_and_renewal_offsets()
    {
        await SeedSourceTenantAsync();

        var summary = await _service.CaptureAsync(
            "restaurant", "Restaurant pack", "Hospitality", null, "restaurant-one", "admin@platform");

        Assert.Equal(1, summary.PackageCount);
        Assert.Equal(1, summary.BenchmarkCount);
        Assert.Equal(1, summary.RenewalCount);
        var template = _templates.Items["restaurant"];
        Assert.InRange(template.Renewals[0].DueInDays, 195, 201); // relative, not absolute
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.TemplateCaptured);
    }

    [Fact]
    public async Task Apply_activates_packages_and_creates_config_for_the_new_tenant()
    {
        await SeedSourceTenantAsync();
        await _service.CaptureAsync("restaurant", "Restaurant pack", null, null, "restaurant-one", "admin@platform");

        var result = await _service.ApplyAsync("restaurant", "new-bistro", "admin@platform");

        Assert.Equal(new[] { "pos-rules@1.0.0" }, result.PackagesActivated);
        Assert.Contains("new-bistro:pos-rules@1.0.0", _loader.Activated);
        Assert.Equal(1, result.BenchmarksCreated);
        Assert.Equal(1, result.RenewalsCreated);
        var renewal = Assert.Single(_renewals.Items.Where(r => r.TenantId == "new-bistro"));
        Assert.True(renewal.DueDate > DateTimeOffset.UtcNow.AddDays(180));
        Assert.Contains(_audit.Items, a =>
            a.Category == AuditCategories.TemplateApplied && a.TenantId == "new-bistro");
    }

    [Fact]
    public async Task Reapplying_is_additive_and_idempotent()
    {
        await SeedSourceTenantAsync();
        await _service.CaptureAsync("restaurant", "Restaurant pack", null, null, "restaurant-one", "admin@platform");
        await _service.ApplyAsync("restaurant", "new-bistro", "admin@platform");
        // Mark the loaded package Active, as the real loader would.
        await _packages.UpdateStateAsync("new-bistro", "pos-rules", "1.0.0", PackageLoadState.Active, null);

        var second = await _service.ApplyAsync("restaurant", "new-bistro", "admin@platform");

        Assert.Empty(second.PackagesActivated);
        Assert.Equal(new[] { "pos-rules@1.0.0" }, second.PackagesSkipped);
        Assert.Equal(0, second.BenchmarksCreated);
        Assert.Equal(0, second.RenewalsCreated);
        Assert.Single(_renewals.Items.Where(r => r.TenantId == "new-bistro")); // no duplicates
    }

    [Fact]
    public void Apply_orders_packages_by_declared_dependencies()
    {
        var baseline = new KnowledgePackageManifest { PackageId = "treasury", PackageVersion = "1.0.0" };
        var dependent = new KnowledgePackageManifest
        {
            PackageId = "irs",
            PackageVersion = "1.0.0",
            Dependencies = [new PackageDependency { PackageId = "treasury", MinimumVersion = "1.0.0" }]
        };
        var unrelated = new KnowledgePackageManifest { PackageId = "gaap", PackageVersion = "1.0.0" };

        // Captured in the worst order: dependent first.
        var ordered = OnboardingTemplateService.OrderByDependencies([dependent, unrelated, baseline]);

        Assert.True(
            ordered.ToList().FindIndex(p => p.PackageId == "treasury")
                < ordered.ToList().FindIndex(p => p.PackageId == "irs"),
            "dependency must come before dependent");
        Assert.Equal(3, ordered.Count);
    }

    [Fact]
    public async Task Applying_a_missing_template_fails_loudly()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.ApplyAsync("nope", "new-bistro", "admin@platform"));
    }
}
