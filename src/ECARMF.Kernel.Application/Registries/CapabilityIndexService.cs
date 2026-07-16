using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

/// <summary>One entry in a tenant's flattened capability index.</summary>
public sealed record CapabilityItem(
    string Kind, string Id, string Name, string? Description, string PackageId);

public interface ICapabilityIndex
{
    /// <summary>Everything a tenant can do and knows — its active packages'
    /// rules, KPIs, agents, entities, events, and knowledge assets — as one
    /// flat, searchable list.</summary>
    Task<IReadOnlyList<CapabilityItem>> ForTenantAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Server-side capability index. Previously the Capability Explorer and the
/// ⌘K palette fetched every active package's full manifest individually
/// (40+ requests on a large tenant); this flattens them in one pass so the
/// UI needs a single call.
/// </summary>
public class CapabilityIndexService : ICapabilityIndex
{
    private readonly IPackageStore _packages;

    public CapabilityIndexService(IPackageStore packages) => _packages = packages;

    public async Task<IReadOnlyList<CapabilityItem>> ForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var active = await _packages.GetByStateAsync(tenantId, PackageLoadState.Active, ct);
        var items = new List<CapabilityItem>();

        foreach (var p in active)
        {
            var m = p.Manifest;
            foreach (var r in m.Rules)
                items.Add(new CapabilityItem("Control", r.RuleId, r.Name is { Length: > 0 } ? r.Name : r.RuleId,
                    r.Description ?? r.OutcomeOnMatch, m.PackageId));
            foreach (var k in m.PerformanceFrameworks.SelectMany(f => f.Kpis))
                items.Add(new CapabilityItem("KPI", k.KpiId, k.Name is { Length: > 0 } ? k.Name : k.KpiId,
                    string.IsNullOrWhiteSpace(k.RiskType) ? k.Description : $"{k.Description} · risk: {k.RiskType}".Trim(' ', '·'),
                    m.PackageId));
            foreach (var a in m.Agents)
                items.Add(new CapabilityItem("Agent", a.AgentId, a.Name is { Length: > 0 } ? a.Name : a.AgentId,
                    a.Description, m.PackageId));
            foreach (var e in m.Entities)
                items.Add(new CapabilityItem("Entity", e.EntityTypeName, e.EntityTypeName, e.Description, m.PackageId));
            foreach (var ev in m.Events)
                items.Add(new CapabilityItem("Event", ev.EventName, ev.EventName, ev.Description, m.PackageId));
            foreach (var ka in m.KnowledgeAssets)
                items.Add(new CapabilityItem("Knowledge", ka.AssetId, ka.Title is { Length: > 0 } ? ka.Title : ka.AssetId,
                    ka.Summary, m.PackageId));
        }

        return items;
    }
}
