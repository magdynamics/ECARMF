namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>
/// Persistence record for an uploaded Knowledge Package. Queryable identity
/// fields are relational columns; the full manifest (entities, events, rules,
/// capabilities) is stored as a JSON document so the schema stays stable as
/// package metadata evolves.
/// </summary>
public class KnowledgePackageRecord
{
    public Guid Id { get; set; }

    public string PackageId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Complete KnowledgePackageManifest serialized as JSON.</summary>
    public string ManifestJson { get; set; } = string.Empty;
}
