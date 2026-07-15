using System.Text.RegularExpressions;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>A package presented as a commercial "skill": a tier, a price, and
/// whether the tenant currently has it. The skill id is the package id.</summary>
public sealed record SkillView(
    string PackageId,
    string Version,
    string DisplayName,
    string Tier,
    decimal MonthlyPrice,
    string Currency,
    string? WhatItDoes,
    int Controls,
    int Kpis,
    int Agents,
    IReadOnlyList<string> Dependencies,
    bool Installed,
    bool Active);

public sealed record SkillActionResult(bool Success, string Message);

/// <summary>Skill tiers, cheapest-to-govern first.</summary>
public static class SkillTiers
{
    public const string Core = "Core";         // included in the base fee (integrations, renewals, statements)
    public const string Industry = "Industry"; // an industry bundle skill
    public const string AddOn = "AddOn";       // premium, metered per skill

    public static readonly string[] Ordered = [Core, Industry, AddOn];
}

public interface ISkillCatalog
{
    /// <summary>Every skill available on the platform, flagged with whether the
    /// given tenant has it installed/active.</summary>
    Task<IReadOnlyList<SkillView>> ListForTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Turn a skill on for a tenant: install it from the library (with
    /// dependencies) if absent, or re-activate it if already present.</summary>
    Task<SkillActionResult> ActivateAsync(string packageId, string tenantId, string actor, CancellationToken ct = default);

    /// <summary>Turn a skill off for a tenant (deactivate its package).</summary>
    Task<SkillActionResult> DeactivateAsync(string packageId, string tenantId, string actor, CancellationToken ct = default);

    /// <summary>Active, priced skills for a tenant — one recurring charge each
    /// (consumed by billing).</summary>
    Task<IReadOnlyList<(string Name, decimal Price)>> ActivePricedSkillsAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// The commercial layer over the package library. A skill IS a knowledge
/// package; this classifies each into a tier with a price and reconciles it
/// against a tenant's active packages, so the operator can add/activate/
/// deactivate skills on a tenant and billing can charge for the priced ones.
/// Classification is code-defined (no per-price table yet) and can be made
/// operator-editable later without changing callers.
/// </summary>
public class SkillCatalogService : ISkillCatalog
{
    private const string DefaultCurrency = "USD";

    private readonly IPackageCatalog _catalog;
    private readonly IPackageStore _packages;
    private readonly IPackageLoader _loader;

    public SkillCatalogService(IPackageCatalog catalog, IPackageStore packages, IPackageLoader loader)
    {
        _catalog = catalog;
        _packages = packages;
        _loader = loader;
    }

    public async Task<IReadOnlyList<SkillView>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var catalog = await _catalog.ListAsync(ct);
        var tenantPackages = await _packages.GetAllAsync(tenantId, ct);
        var tenantState = tenantPackages
            .GroupBy(p => p.Manifest.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(p => p.State == PackageLoadState.Active), StringComparer.OrdinalIgnoreCase);

        // One skill per package id (the highest version in the library).
        return catalog
            .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.PackageVersion, VersionComparer).First())
            .Select(e =>
            {
                var (tier, price) = Classify(e.PackageId, e.Name);
                var installed = tenantState.ContainsKey(e.PackageId);
                return new SkillView(
                    e.PackageId, e.PackageVersion, Friendly(e.Name), tier, price, DefaultCurrency,
                    e.Description, e.Controls, e.Kpis, e.Agents, e.Dependencies,
                    installed, installed && tenantState[e.PackageId]);
            })
            .OrderBy(s => Array.IndexOf(SkillTiers.Ordered, s.Tier))
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SkillActionResult> ActivateAsync(string packageId, string tenantId, string actor, CancellationToken ct = default)
    {
        // Already present? re-activate the stored version. Otherwise install
        // from the library (bringing dependencies).
        var existing = (await _packages.GetAllAsync(tenantId, ct))
            .Where(p => string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existing.Count > 0)
        {
            if (existing.Any(p => p.State == PackageLoadState.Active))
                return new SkillActionResult(true, "Skill is already active.");

            var stored = existing[0];
            var result = await _loader.ActivateAsync(tenantId, stored.Manifest.PackageId, stored.Manifest.PackageVersion, ct);
            return result.Success
                ? new SkillActionResult(true, "Skill activated.")
                : new SkillActionResult(false, string.Join("; ", result.Errors));
        }

        var entry = (await _catalog.ListAsync(ct))
            .Where(e => string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.PackageVersion, VersionComparer)
            .FirstOrDefault();
        if (entry is null)
            return new SkillActionResult(false, $"Skill '{packageId}' is not in the library.");

        var install = await _catalog.InstallAsync(packageId, entry.PackageVersion, tenantId, actor, withDependencies: true, ct);
        return install.Errors.Count == 0
            ? new SkillActionResult(true, $"Skill installed and activated ({install.Activated.Count} package(s)).")
            : new SkillActionResult(false, string.Join("; ", install.Errors));
    }

    public async Task<SkillActionResult> DeactivateAsync(string packageId, string tenantId, string actor, CancellationToken ct = default)
    {
        var active = (await _packages.GetAllAsync(tenantId, ct))
            .FirstOrDefault(p => string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && p.State == PackageLoadState.Active);
        if (active is null)
            return new SkillActionResult(true, "Skill is already off.");

        var result = await _loader.DeactivateAsync(tenantId, active.Manifest.PackageId, active.Manifest.PackageVersion, ct);
        return result.Success
            ? new SkillActionResult(true, "Skill deactivated.")
            : new SkillActionResult(false, string.Join("; ", result.Errors));
    }

    public async Task<IReadOnlyList<(string Name, decimal Price)>> ActivePricedSkillsAsync(string tenantId, CancellationToken ct = default)
    {
        var active = (await _packages.GetByStateAsync(tenantId, PackageLoadState.Active, ct));
        return active
            .Select(p => (p.Manifest.PackageId, p.Manifest.Name))
            .DistinctBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var (_, price) = Classify(x.PackageId, x.Name);
                return (Name: Friendly(x.Name), Price: price);
            })
            .Where(x => x.Price > 0)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Code-defined tier + monthly price. Core (integrations,
    /// renewals, statements, foundations) is included; add-ons (autonomous
    /// orchestration, financial continuity) are metered; everything else is an
    /// industry skill. Prices are defaults an operator can revise later.</summary>
    public static (string Tier, decimal Price) Classify(string packageId, string name)
    {
        var s = (packageId + " " + name).ToLowerInvariant();

        if (s.Contains("foundation") || s.Contains("integration") || s.Contains("connector")
            || s.Contains("renewal") || s.Contains("banking") || s.Contains("accounting"))
            return (SkillTiers.Core, 0m);

        if (s.Contains("autonomous") || s.Contains("orchestration") || s.Contains("remediation")
            || s.Contains("continuity") || s.Contains("liquidity"))
            return (SkillTiers.AddOn, 1500m);

        return (SkillTiers.Industry, 500m);
    }

    private static string Friendly(string name)
    {
        var n = Regex.Replace(name, @"^TCEL\s*", string.Empty, RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"T9-\d+[-\s]*", string.Empty, RegexOptions.IgnoreCase);
        n = n.Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(n) ? name : n;
    }

    private static readonly IComparer<string> VersionComparer =
        Comparer<string>.Create((a, b) =>
        {
            static (int, int, int) P(string v)
            {
                var p = v.Split('.');
                int G(int i) => p.Length > i && int.TryParse(p[i], out var n) ? n : 0;
                return (G(0), G(1), G(2));
            }
            var c = P(a).CompareTo(P(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });
}
