namespace ECARMF.Kernel.Domain.Tenancy;

/// <summary>
/// An IT asset under management (Batch 2, Refinement 9) — one new entity,
/// NO new mechanisms: certificate/license expiration reuses
/// ComplianceRenewal with subjectType "ITAsset"; vulnerability/patch/backup
/// posture arrives as ScoreRecords tagged with riskType (Refinement 11).
/// </summary>
public class ITAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable slug, e.g. "prod-sql-01", "wildcard-cert-2026".</summary>
    public string AssetId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Open type: Server, CloudResource, Hardware, NetworkDevice,
    /// License, Certificate — or any future type.</summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Org unit responsible for the asset.</summary>
    public string? OwnerUnitId { get; set; }

    public string? Environment { get; set; }

    public string? Notes { get; set; }

    /// <summary>Active | Retired.</summary>
    public string Status { get; set; } = "Active";

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
