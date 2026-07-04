namespace ECARMF.Kernel.Domain.Entities;

/// <summary>
/// ECARMF Universal Base Entity (ECARMF-002, WP-02).
/// Every managed object in the ECARMF ecosystem inherits from this type.
/// Field set conforms to schemas/meta-model/base-entity.schema.json.
/// </summary>
public abstract class UniversalBaseEntity
{
    public Guid EntityId { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Version { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> Relationships { get; set; } = [];

    public List<string> EvidenceReferences { get; set; } = [];

    public Dictionary<string, string> Metadata { get; set; } = [];
}
