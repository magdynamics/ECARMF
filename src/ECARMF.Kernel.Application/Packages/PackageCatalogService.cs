using System.Text.Json;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Onboarding;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>One distinct package available on the platform (a package that has
/// been loaded into at least one tenant), summarised for the operator catalog.
/// "Controls" surfaces the executable rules; the knowledge catalogs often carry
/// a much larger control library as reference (see the package detail).</summary>
public sealed record CatalogEntry(
    string PackageId,
    string PackageVersion,
    string Name,
    string Publisher,
    string? Description,
    IReadOnlyList<string> Dependencies,
    int Entities,
    int Controls,
    int Events,
    int Agents,
    int Kpis,
    int KnowledgeAssets,
    IReadOnlyList<string> InstalledInTenants);

public sealed record CatalogInstallResult(
    IReadOnlyList<string> Activated,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Errors);

public interface IPackageCatalog
{
    /// <summary>Every distinct (packageId, version) loaded anywhere on the
    /// platform — the library an operator can install from.</summary>
    Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>Full manifest for one catalog entry (for the detail view).</summary>
    Task<KnowledgePackageManifest?> GetManifestAsync(string packageId, string version, CancellationToken ct = default);

    /// <summary>Install a catalog package into a target tenant: clone the
    /// manifest, load and activate it (and, when requested, its declared
    /// dependencies), through the same machinery as manual setup. Additive and
    /// idempotent — a package already present is skipped, never duplicated.</summary>
    Task<CatalogInstallResult> InstallAsync(
        string packageId, string version, string toTenantId, string actor,
        bool withDependencies, CancellationToken ct = default);
}

/// <summary>
/// The platform package library. Packages are authored per tenant, but a
/// manifest loaded into ANY tenant is a proven, installable unit — so the
/// catalog is the union of every package across all tenants, and installing is
/// "copy this manifest into that tenant and activate it". This is what makes a
/// capability like TCEL T9-041/T9-042 a platform-level offering instead of a
/// tenant-locked one. Operator-only; the endpoints enforce that.
/// </summary>
public class PackageCatalogService : IPackageCatalog
{
    private readonly IPackageStore _packages;
    private readonly IPackageLoader _loader;
    private readonly IAuditLog _audit;

    public PackageCatalogService(IPackageStore packages, IPackageLoader loader, IAuditLog audit)
    {
        _packages = packages;
        _loader = loader;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken ct = default)
    {
        var all = await _packages.GetAllAcrossTenantsAsync(ct);
        return all
            .GroupBy(p => (p.Manifest.PackageId, p.Manifest.PackageVersion))
            .Select(g =>
            {
                var m = g.First().Manifest;
                var installedIn = g
                    .Where(p => p.State == PackageLoadState.Active)
                    .Select(p => p.TenantId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t)
                    .ToList();
                return new CatalogEntry(
                    m.PackageId, m.PackageVersion, m.Name, m.Publisher, m.Description,
                    m.Dependencies.Select(d => d.PackageId).ToList(),
                    m.Entities.Count, m.Rules.Count, m.Events.Count, m.Agents.Count,
                    m.PerformanceFrameworks.Sum(f => f.Kpis.Count), m.KnowledgeAssets.Count,
                    installedIn);
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<KnowledgePackageManifest?> GetManifestAsync(string packageId, string version, CancellationToken ct = default)
    {
        var all = await _packages.GetAllAcrossTenantsAsync(ct);
        return FindManifest(all, packageId, version);
    }

    public async Task<CatalogInstallResult> InstallAsync(
        string packageId, string version, string toTenantId, string actor,
        bool withDependencies, CancellationToken ct = default)
    {
        var all = await _packages.GetAllAcrossTenantsAsync(ct);

        // Best (highest-version) manifest for each packageId, so a dependency
        // resolves even if the requested package pins only a minimum version.
        var bestById = all
            .GroupBy(p => p.Manifest.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.Manifest).OrderByDescending(m => m.PackageVersion, VersionComparer).First(),
                StringComparer.OrdinalIgnoreCase);

        var target = FindManifest(all, packageId, version);
        if (target is null)
            return new CatalogInstallResult([], [], [$"Package '{packageId}' version '{version}' is not in the catalog."]);

        // Gather the target plus, optionally, its transitive dependencies.
        var toInstall = new Dictionary<string, KnowledgePackageManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [target.PackageId] = target
        };
        if (withDependencies)
        {
            var queue = new Queue<KnowledgePackageManifest>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                foreach (var dep in queue.Dequeue().Dependencies)
                {
                    if (toInstall.ContainsKey(dep.PackageId)) continue;
                    if (bestById.TryGetValue(dep.PackageId, out var depManifest))
                    {
                        toInstall[dep.PackageId] = depManifest;
                        queue.Enqueue(depManifest);
                    }
                }
            }
        }

        var activated = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        // Dependency order: a package validates only after the packages whose
        // events its rules trigger on are active.
        foreach (var manifest in OnboardingTemplateService.OrderByDependencies(toInstall.Values.ToList()))
        {
            var reference = $"{manifest.PackageId}@{manifest.PackageVersion}";
            try
            {
                if (await _packages.ExistsAsync(toTenantId, manifest.PackageId, manifest.PackageVersion, ct))
                {
                    skipped.Add(reference);
                    continue;
                }

                // Deep-clone so the target tenant gets its own manifest instance
                // and entity identity, never a row shared with the source tenant.
                var clone = Clone(manifest);
                clone.EntityId = Guid.NewGuid();
                clone.TenantId = toTenantId;

                var loaded = await _loader.LoadAsync(toTenantId, clone, ct);
                if (!loaded.Success)
                {
                    errors.Add($"{reference}: load failed — {string.Join("; ", loaded.Errors)}");
                    continue;
                }

                var active = await _loader.ActivateAsync(toTenantId, clone.PackageId, clone.PackageVersion, ct);
                if (active.Success) activated.Add(reference);
                else errors.Add($"{reference}: activation failed — {string.Join("; ", active.Errors)}");
            }
            catch (Exception ex)
            {
                errors.Add($"{reference}: {ex.Message}");
            }
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = toTenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.PackageInstalledFromCatalog,
            Actor = actor,
            Summary = $"Installed '{packageId}@{version}' from the platform catalog into '{toTenantId}': "
                + $"{activated.Count} activated, {skipped.Count} already present"
                + (errors.Count > 0 ? $", {errors.Count} error(s)." : "."),
            Detail = new Dictionary<string, string>
            {
                ["packageId"] = packageId,
                ["version"] = version,
                ["withDependencies"] = withDependencies.ToString(),
                ["activated"] = string.Join(", ", activated),
                ["skipped"] = string.Join(", ", skipped),
                ["errors"] = string.Join(" | ", errors)
            }
        }, ct);

        return new CatalogInstallResult(activated, skipped, errors);
    }

    private static KnowledgePackageManifest? FindManifest(
        IReadOnlyList<StoredPackage> all, string packageId, string version) =>
        all.Select(p => p.Manifest).FirstOrDefault(m =>
            string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.PackageVersion, version, StringComparison.OrdinalIgnoreCase));

    private static KnowledgePackageManifest Clone(KnowledgePackageManifest manifest) =>
        JsonSerializer.Deserialize<KnowledgePackageManifest>(JsonSerializer.Serialize(manifest))!;

    /// <summary>Compares "major.minor.patch" numerically, falling back to
    /// ordinal for anything that doesn't parse.</summary>
    private static readonly IComparer<string> VersionComparer =
        Comparer<string>.Create((a, b) =>
        {
            static (int, int, int) Parse(string v)
            {
                var parts = v.Split('.');
                int P(int i) => parts.Length > i && int.TryParse(parts[i], out var n) ? n : 0;
                return (P(0), P(1), P(2));
            }
            var pa = Parse(a);
            var pb = Parse(b);
            var c = pa.CompareTo(pb);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });
}
