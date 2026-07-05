namespace ECARMF.Kernel.Domain.Tenancy;

/// <summary>
/// One node of a tenant's organizational shape. Self-referencing and
/// type-free on purpose: Tenant → Division → Location → Property, or
/// Tenant → Region → Project → Subcontractor, or any other shape and depth
/// — the hierarchy is DATA, never schema. Packages and frameworks attach
/// per unit, so a dental location and a warehouse in the same tenant can
/// run entirely different intelligence.
/// </summary>
public class OrganizationalUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable slug used in references (e.g. "orland-park-clinic").</summary>
    public string UnitId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Tenant-defined: Division, LegalEntity, Location, Property,
    /// Region, Project — any label the tenant's world uses.</summary>
    public string UnitType { get; set; } = string.Empty;

    /// <summary>Null for a root unit directly under the tenant.</summary>
    public string? ParentUnitId { get; set; }

    /// <summary>Industry classification driving framework/package
    /// suggestions (e.g. "dental", "restaurant", "real-estate").</summary>
    public string? Industry { get; set; }

    /// <summary>PackageIds attached to this unit. The tenant's Active
    /// version of each package applies; versions stay managed by the
    /// package lifecycle, not duplicated here.</summary>
    public List<string> AttachedPackageIds { get; set; } = [];

    public string? Notes { get; set; }

    /// <summary>Open lifecycle state (Batch 1, Refinement 4):
    /// PreDevelopment, Construction, Stabilization, Operating, Closed,
    /// Sold — or any future state. A state change on ANY unit type triggers
    /// a framework/package review suggestion generically, never
    /// special-cased per unit type.</summary>
    public string LifecycleState { get; set; } = "Operating";

    /// <summary>Active | Archived.</summary>
    public string Status { get; set; } = "Active";

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
