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
}
