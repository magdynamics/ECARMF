using ECARMF.Kernel.Domain.Entities;

namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// The executable unit of the ECARMF meta-model. A Knowledge Package declares
/// the entities, events, rules, and capabilities it contributes to the kernel.
/// The kernel executes these declarations as metadata; a package never
/// contains code and the kernel never encodes domain knowledge itself.
/// </summary>
public class KnowledgePackageManifest : UniversalBaseEntity
{
    public string PackageId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Semantic version of the package (major.minor.patch), distinct
    /// from <see cref="UniversalBaseEntity.Version"/> which tracks the entity record.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public List<PackageDependency> Dependencies { get; set; } = [];

    public List<EntityDeclaration> Entities { get; set; } = [];

    public List<EventDeclaration> Events { get; set; } = [];

    public List<RuleDeclaration> Rules { get; set; } = [];

    public List<CapabilityDeclaration> Capabilities { get; set; } = [];

    public List<SchemaTemplateDeclaration> SchemaTemplates { get; set; } = [];

    public List<PerformanceFrameworkDeclaration> PerformanceFrameworks { get; set; } = [];

    public List<WorkflowDeclaration> Workflows { get; set; } = [];

    public List<AgentDeclaration> Agents { get; set; } = [];

    public List<KnowledgeAsset> KnowledgeAssets { get; set; } = [];

    public List<AIExtractionTemplateDeclaration> AiExtractionTemplates { get; set; } = [];

    /// <summary>Earlier package drafts this one explicitly replaces (TCEL
    /// P2.1). Declared by the REPLACING package only — the superseded manifest
    /// is immutable history. "SupersededBy" is never stored; it is derived by
    /// scanning other manifests' Supersedes (one source of truth, no drift).</summary>
    public List<PackageReference> Supersedes { get; set; } = [];

    /// <summary>Package ids this one aggregates/summarizes while they stay
    /// active (TCEL P2.3) — distinct from Supersedes, which replaces. The
    /// "consolidation is real" check (P3.2) validates these against content.</summary>
    public List<string> Consolidates { get; set; } = [];
}

/// <summary>A reference to another package by id and optional exact version
/// (TCEL P2.1). Unlike <see cref="PackageDependency"/> (a minimum version that
/// must be active), this is a pointer to a specific prior draft.</summary>
public class PackageReference
{
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Exact version referenced; null means "any version of it".</summary>
    public string? PackageVersion { get; set; }
}
