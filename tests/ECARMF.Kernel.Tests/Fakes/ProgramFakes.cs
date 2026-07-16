using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Cases;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Risk;
using ECARMF.Kernel.Domain.Cases;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Risk;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Tests.Fakes;

// Shared fakes for the production-program test suite (work order Phase 2).
// Same style as InMemoryStores: list-backed, tenant-scoped, no behavior
// beyond the contract.

public class InMemoryTenantDirectory : ITenantDirectory
{
    public List<TenantProfile> Items { get; } = [];

    public Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t =>
            string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<TenantProfile>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantProfile>>(Items.ToList());

    public Task AddAsync(TenantProfile profile, CancellationToken ct = default)
    {
        Items.Add(profile);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TenantProfile profile, CancellationToken ct = default)
    {
        Items.RemoveAll(t => string.Equals(t.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase));
        Items.Add(profile);
        return Task.CompletedTask;
    }
}

public class InMemoryRenewalStore : IRenewalStore
{
    public List<RenewalCommitment> Items { get; } = [];

    public Task<RenewalCommitment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(r =>
            string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) && r.Id == id));

    public Task<IReadOnlyList<RenewalCommitment>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RenewalCommitment>>(
            Items.Where(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<RenewalCommitment>> GetActiveAllTenantsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RenewalCommitment>>(
            Items.Where(r => r.Status == RenewalStatuses.Active).ToList());

    public Task AddAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        Items.Add(renewal);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        Items.RemoveAll(r => r.Id == renewal.Id);
        Items.Add(renewal);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        Items.RemoveAll(r => string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) && r.Id == id);
        return Task.CompletedTask;
    }
}

public class InMemoryCaseStore : ICaseStore
{
    public List<Case> Items { get; } = [];

    public Task<IReadOnlyList<Case>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Case>>(
            Items.Where(c => string.Equals(c.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<Case?> GetAsync(string tenantId, string caseId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(c =>
            string.Equals(c.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.CaseId, caseId, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Case c, CancellationToken ct = default)
    {
        Items.Add(c);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Case c, CancellationToken ct = default)
    {
        Items.RemoveAll(x => x.Id == c.Id);
        Items.Add(c);
        return Task.CompletedTask;
    }
}

public class InMemoryRiskTreatmentStore : IRiskTreatmentStore
{
    public List<RiskTreatment> Items { get; } = [];

    public Task<IReadOnlyList<RiskTreatment>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RiskTreatment>>(
            Items.Where(t => string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<RiskTreatment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t =>
            string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) && t.Id == id));

    public Task<RiskTreatment?> GetByRiskKeyAsync(string tenantId, string riskKey, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t =>
            string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.RiskKey, riskKey, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(RiskTreatment treatment, CancellationToken ct = default)
    {
        Items.Add(treatment);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RiskTreatment treatment, CancellationToken ct = default)
    {
        Items.RemoveAll(t => t.Id == treatment.Id);
        Items.Add(treatment);
        return Task.CompletedTask;
    }
}

public class InMemorySkillSettingStore : ISkillSettingStore
{
    private readonly Dictionary<string, SkillSetting> _settings = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, SkillSetting>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, SkillSetting>>(
            new Dictionary<string, SkillSetting>(_settings, StringComparer.OrdinalIgnoreCase));

    public Task UpsertAsync(SkillSetting setting, string actor, CancellationToken ct = default)
    {
        _settings[setting.SkillId] = setting;
        return Task.CompletedTask;
    }
}

/// <summary>Package loader stub: records the calls; every operation succeeds
/// unless a package id is put on the FailActivation list.</summary>
public class StubPackageLoader : IPackageLoader
{
    private readonly IPackageStore _store;
    public List<string> Loaded { get; } = [];
    public List<string> Activated { get; } = [];
    public List<string> Deactivated { get; } = [];
    public HashSet<string> FailActivation { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StubPackageLoader(IPackageStore store) => _store = store;

    public async Task<PackageOperationResult> LoadAsync(string tenantId, KnowledgePackageManifest manifest, CancellationToken ct = default)
    {
        Loaded.Add($"{manifest.PackageId}@{manifest.PackageVersion}");
        await _store.AddAsync(tenantId, manifest, PackageLoadState.Staged, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Staged);
    }

    public async Task<PackageOperationResult> ActivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        if (FailActivation.Contains(packageId))
            return PackageOperationResult.Fail(PackageLoadState.Failed, $"forced failure for {packageId}");
        Activated.Add($"{packageId}@{packageVersion}");
        await _store.UpdateStateAsync(tenantId, packageId, packageVersion, PackageLoadState.Active, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Active);
    }

    public async Task<PackageOperationResult> DeactivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        Deactivated.Add($"{packageId}@{packageVersion}");
        await _store.UpdateStateAsync(tenantId, packageId, packageVersion, PackageLoadState.Deactivated, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Deactivated);
    }

    public Task RehydrateActiveAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Canned package catalog for advisor/skill tests: entries are set by
/// the test; installs are recorded and always succeed.</summary>
public class StubPackageCatalog : IPackageCatalog
{
    public List<CatalogEntry> Entries { get; } = [];
    public List<(string PackageId, string Version, string Tenant, bool WithDeps)> Installs { get; } = [];

    public static CatalogEntry Entry(string packageId, string name,
        string version = "1.0.0", int controls = 1, int kpis = 0, int agents = 0,
        string[]? deps = null, string[]? installedIn = null) =>
        new(packageId, version, name, "Test", null, deps ?? [], 1, controls, 0, agents, kpis, 0,
            installedIn ?? []);

    public Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CatalogEntry>>(Entries.ToList());

    public Task<KnowledgePackageManifest?> GetManifestAsync(string packageId, string version, CancellationToken ct = default) =>
        Task.FromResult<KnowledgePackageManifest?>(null);

    public Task<CatalogInstallResult> InstallAsync(
        string packageId, string version, string toTenantId, string actor,
        bool withDependencies, CancellationToken ct = default)
    {
        Installs.Add((packageId, version, toTenantId, withDependencies));
        return Task.FromResult(new CatalogInstallResult([$"{packageId}@{version}"], [], []));
    }
}

/// <summary>Canned platform-risk overview for action-center tests.</summary>
public class StubPlatformRisk : IPlatformRiskService
{
    public PlatformRiskOverview Overview { get; set; } =
        new(0, 0, 0, 0, [], []);

    public Task<PlatformRiskOverview> OverviewAsync(CancellationToken ct = default) =>
        Task.FromResult(Overview);
}
