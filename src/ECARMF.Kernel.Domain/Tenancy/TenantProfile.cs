namespace ECARMF.Kernel.Domain.Tenancy;

/// <summary>
/// A client of the platform. The platform operator (acting from the reserved
/// 'platform' tenant) onboards clients as tenant profiles; every tenant's
/// data, users, credentials, packages, and AI configuration are isolated by
/// TenantId. A suspended tenant is locked out of the entire API.
/// </summary>
public class TenantProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The tenant identifier used on every record (slug, e.g. "acme-capital").</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Client company name.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Industry { get; set; }

    /// <summary>Primary business contact at the client.</summary>
    public string? ContactName { get; set; }

    public string? ContactEmail { get; set; }

    /// <summary>Active | Suspended.</summary>
    public string Status { get; set; } = TenantStatus.Active;

    /// <summary>Assigned billing plan (see BillingPlan); null = default plan.</summary>
    public string? BillingPlanId { get; set; }

    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class TenantStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
}
