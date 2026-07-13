using System.Text;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>
/// Deliberately dumb, deterministic, offline heuristics for the TCEL Phase-3
/// warnings (P3.1 agent semantic-overlap, P3.2 consolidation-is-real). These
/// NEVER call an LLM and NEVER block a load — the kernel cannot judge
/// semantics, so their whole job is to raise a flag a human confirms. Four
/// TCEL agents overlapped legitimately and one decomposition was correct, so
/// a false positive here must cost nothing.
/// </summary>
public static class PackageHeuristics
{
    // Enough stopwords to strip the obvious glue; the length filter (< 4)
    // removes "ai", "the", "for", etc. without an exhaustive list.
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "over",
        "your", "you", "are", "any", "all", "not", "never", "which", "when",
        "agent", "data", "package", "tenant", "kernel"
    };

    private const int MinSharedTerms = 2;
    private const double MinContainment = 0.34;

    /// <summary>Significant lowercase tokens (length ≥ 4, non-stopword) drawn
    /// from the given texts.</summary>
    public static HashSet<string> SignificantTerms(params string?[] texts)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var token = new StringBuilder();
            foreach (var ch in text)
            {
                if (char.IsLetter(ch))
                {
                    token.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    Flush(token, terms);
                }
            }
            Flush(token, terms);
        }
        return terms;

        void Flush(StringBuilder token, HashSet<string> into)
        {
            if (token.Length >= 4)
            {
                var word = token.ToString();
                if (!Stopwords.Contains(word)) into.Add(word);
            }
            token.Clear();
        }
    }

    /// <summary>Warns when an incoming agent's scope terms overlap an already
    /// active agent's (P3.1). Compares AgentId/Name/Description/Persona; fires
    /// only on a real cluster of shared terms, never a single incidental word.</summary>
    public static IReadOnlyList<string> AgentOverlapWarnings(
        IReadOnlyList<Registered<AgentDeclaration>> active, KnowledgePackageManifest incoming)
    {
        var warnings = new List<string>();
        if (active.Count == 0) return warnings;

        foreach (var agent in incoming.Agents)
        {
            var incomingTerms = SignificantTerms(agent.AgentId, agent.Name, agent.Description, agent.Persona);
            if (incomingTerms.Count == 0) continue;

            foreach (var existing in active)
            {
                // Exact-id reuse is a hard registry conflict at activation, not
                // a semantic-overlap nudge — skip it here.
                if (string.Equals(existing.Declaration.AgentId, agent.AgentId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var existingTerms = SignificantTerms(
                    existing.Declaration.AgentId, existing.Declaration.Name,
                    existing.Declaration.Description, existing.Declaration.Persona);
                if (existingTerms.Count == 0) continue;

                var shared = incomingTerms.Intersect(existingTerms, StringComparer.OrdinalIgnoreCase).ToList();
                var containment = (double)shared.Count / Math.Min(incomingTerms.Count, existingTerms.Count);

                if (shared.Count >= MinSharedTerms && containment >= MinContainment)
                {
                    warnings.Add(
                        $"Agent '{agent.AgentId}' overlaps registered agent '{existing.Declaration.AgentId}' " +
                        $"({existing.PackageId}@{existing.PackageVersion}): shared scope terms " +
                        $"[{string.Join(", ", shared.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}]. " +
                        "Confirm the boundary or record the decomposition decision.");
                }
            }
        }

        return warnings;
    }

    /// <summary>True when the incoming manifest's content literally references
    /// the consolidated package by id or by any id that package declared
    /// (P3.2). A tripwire, not proof of real consolidation.</summary>
    public static bool ReferencesConsolidated(
        KnowledgePackageManifest incoming, string consolidatedPackageId, KnowledgePackageManifest consolidatedManifest)
    {
        var text = ContentSearchText(incoming);
        if (text.Contains(consolidatedPackageId.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return true;
        }
        return DeclaredIds(consolidatedManifest)
            .Any(id => text.Contains(id.ToLowerInvariant(), StringComparison.Ordinal));
    }

    /// <summary>Every declared id in a manifest, across all kinds.</summary>
    public static IReadOnlyList<string> DeclaredIds(KnowledgePackageManifest m)
    {
        var ids = new List<string?>();
        ids.AddRange(m.Entities.Select(e => e.EntityTypeName));
        ids.AddRange(m.Events.Select(e => e.EventName));
        ids.AddRange(m.Rules.Select(r => r.RuleId));
        ids.AddRange(m.Capabilities.Select(c => c.CapabilityId));
        ids.AddRange(m.SchemaTemplates.Select(t => t.TemplateId));
        ids.AddRange(m.PerformanceFrameworks.Select(f => f.FrameworkId));
        ids.AddRange(m.PerformanceFrameworks.SelectMany(f => f.Kpis.Select(k => k.KpiId)));
        ids.AddRange(m.Workflows.Select(w => w.WorkflowId));
        ids.AddRange(m.Agents.Select(a => a.AgentId));
        ids.AddRange(m.KnowledgeAssets.Select(a => a.AssetId));
        ids.AddRange(m.KnowledgeAssets.Select(a => a.DocKey));
        ids.AddRange(m.AiExtractionTemplates.Select(t => t.TemplateId));
        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Lowercased blob of the manifest's CONTENT — deliberately
    /// excludes Consolidates/Supersedes/Dependencies so a package cannot
    /// "reference" a consolidated package merely by naming it in the
    /// consolidates list.</summary>
    private static string ContentSearchText(KnowledgePackageManifest m)
    {
        var sb = new StringBuilder();
        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s)) sb.Append(s).Append(' '); }

        foreach (var e in m.Entities) Add(e.EntityTypeName);
        foreach (var e in m.Events) Add(e.EventName);
        foreach (var r in m.Rules)
        {
            Add(r.RuleId); Add(r.Name); Add(r.Description); Add(r.TriggerEvent);
            Add(r.OutcomeOnMatch); Add(r.ReasonTemplate);
            foreach (var c in r.Conditions) { Add(c.Field); Add(c.Value); }
            foreach (var s in r.EmitScores) { Add(s.ScoreType); Add(s.Value); Add(s.SubjectType); }
        }
        foreach (var c in m.Capabilities) { Add(c.CapabilityId); Add(c.Name); Add(c.Description); }
        foreach (var t in m.SchemaTemplates) { Add(t.TemplateId); Add(t.Name); Add(t.TargetEntityType); }
        foreach (var f in m.PerformanceFrameworks)
        {
            Add(f.FrameworkId); Add(f.Name); Add(f.Description);
            foreach (var k in f.Kpis) { Add(k.KpiId); Add(k.Name); Add(k.Formula); Add(k.Description); }
        }
        foreach (var w in m.Workflows)
        {
            Add(w.WorkflowId); Add(w.TriggerEvent);
            foreach (var s in w.Steps) Add(s.Target);
        }
        foreach (var a in m.Agents) { Add(a.AgentId); Add(a.Name); Add(a.Description); Add(a.Persona); }
        foreach (var a in m.KnowledgeAssets)
        {
            Add(a.AssetId); Add(a.DocKey); Add(a.Title); Add(a.Summary); Add(a.ContentText);
            foreach (var rel in a.Relationships) Add(rel.RelatedAssetId);
        }
        foreach (var t in m.AiExtractionTemplates) { Add(t.TemplateId); Add(t.TargetType); }

        return sb.ToString().ToLowerInvariant();
    }
}
