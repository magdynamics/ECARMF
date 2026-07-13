using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>One registered id and the package version(s) that declare it.</summary>
public sealed record LedgerEntry(string Id, IReadOnlyList<string> DeclaredBy);

/// <summary>
/// The tenant's ID ledger (TCEL P1.2): every id in use per kind, with
/// <c>packageId@version</c> provenance, plus warnings for any dependency cycle
/// already sitting in the stored set. It is a PROJECTION over stored packages,
/// never persisted — so it cannot go stale the way TCEL's hand-maintained
/// numbering did. An authoring pipeline polls this before generating the next
/// wave and treats every listed id as reserved.
/// </summary>
public sealed record PackageIdLedger(
    IReadOnlyDictionary<string, IReadOnlyList<LedgerEntry>> Ids,
    IReadOnlyList<string> Warnings);

public interface IPackageIdLedgerService
{
    Task<PackageIdLedger> BuildAsync(string tenantId, CancellationToken ct = default);
}

public sealed class PackageIdLedgerService : IPackageIdLedgerService
{
    private readonly IPackageStore _store;

    public PackageIdLedgerService(IPackageStore store) => _store = store;

    public async Task<PackageIdLedger> BuildAsync(string tenantId, CancellationToken ct = default)
    {
        // Every stored package, any state — staged and inactive versions must
        // show, so a collision is visible BEFORE activation rejects it.
        var stored = await _store.GetAllAsync(tenantId, ct);

        var kinds = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

        void Record(string kind, string? id, string provenance)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (!kinds.TryGetValue(kind, out var byId))
            {
                byId = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                kinds[kind] = byId;
            }
            if (!byId.TryGetValue(id, out var provenances))
            {
                provenances = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                byId[id] = provenances;
            }
            provenances.Add(provenance);
        }

        foreach (var package in stored)
        {
            var m = package.Manifest;
            var provenance = $"{m.PackageId}@{m.PackageVersion}";

            foreach (var e in m.Entities) Record("entities", e.EntityTypeName, provenance);
            foreach (var e in m.Events) Record("events", e.EventName, provenance);
            foreach (var r in m.Rules) Record("rules", r.RuleId, provenance);
            foreach (var c in m.Capabilities) Record("capabilities", c.CapabilityId, provenance);
            foreach (var t in m.SchemaTemplates) Record("schemaTemplates", t.TemplateId, provenance);
            foreach (var f in m.PerformanceFrameworks) Record("performanceFrameworks", f.FrameworkId, provenance);
            foreach (var w in m.Workflows) Record("workflows", w.WorkflowId, provenance);
            foreach (var a in m.Agents) Record("agents", a.AgentId, provenance);
            foreach (var k in m.KnowledgeAssets) Record("knowledgeAssets", k.AssetId, provenance);
            foreach (var x in m.AiExtractionTemplates) Record("aiExtractionTemplates", x.TemplateId, provenance);
        }

        var ids = kinds.ToDictionary(
            kind => kind.Key,
            kind => (IReadOnlyList<LedgerEntry>)kind.Value
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new LedgerEntry(kv.Key, kv.Value.ToList()))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        var warnings = DetectStoredCycles(stored);
        return new PackageIdLedger(ids, warnings);
    }

    /// <summary>Every dependency cycle already present among stored packages,
    /// as readable paths — the pre-existing-state warnings P1.1 deliberately
    /// leaves for this report rather than blocking unrelated loads.</summary>
    private static IReadOnlyList<string> DetectStoredCycles(IReadOnlyList<StoredPackage> stored)
    {
        var graph = stored
            .GroupBy(p => p.Manifest.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(p => p.Manifest.Dependencies.Select(d => d.PackageId))
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        var seenCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0 white, 1 gray, 2 black
        var path = new List<string>();

        bool Visit(string node)
        {
            color[node] = 1;
            path.Add(node);
            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var next in deps.Where(graph.ContainsKey))
                {
                    var c = color.GetValueOrDefault(next, 0);
                    if (c == 1)
                    {
                        var start = path.IndexOf(next);
                        var loop = path.Skip(start).Append(next).ToList();
                        var key = string.Join("→", loop.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                        if (seenCycles.Add(key))
                        {
                            warnings.Add($"Dependency cycle among stored packages: {string.Join(" → ", loop)}.");
                        }
                    }
                    else if (c == 0)
                    {
                        Visit(next);
                    }
                }
            }
            color[node] = 2;
            path.RemoveAt(path.Count - 1);
            return false;
        }

        foreach (var node in graph.Keys)
        {
            if (color.GetValueOrDefault(node, 0) == 0)
            {
                Visit(node);
            }
        }

        return warnings;
    }
}
