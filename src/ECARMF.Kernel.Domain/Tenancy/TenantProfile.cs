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

    /// <summary>Standard | Elevated | HighSensitivity | Regulated (Batch 1,
    /// Refinement 6). Higher tiers apply stricter defaults automatically:
    /// Elevated+ narrows audit visibility to oversight roles;
    /// HighSensitivity+ refuses header-asserted identity (access key
    /// mandatory); Regulated additionally blocks deletion of watched
    /// obligations (cancel preserves the record instead).</summary>
    public string SensitivityTier { get; set; } = SensitivityTiers.Standard;

    // --- Tenant-Aware Shell branding (ECARMF-ADR-UIUX-001 §2.1, made
    // data-driven). Persisted here so onboarding a tenant never requires a
    // frontend code edit; the UI's static tenantConfig.ts is only a fallback. ---

    /// <summary>Display name for this tenant's workspace (defaults to Name).</summary>
    public string? Brand { get; set; }

    /// <summary>Short industry/segment label shown next to the brand.</summary>
    public string? Segment { get; set; }

    /// <summary>Accent colour (CSS hex, e.g. "#2fbf9f") applied to the shell.</summary>
    public string? AccentColor { get; set; }

    /// <summary>True when this tenant handles PHI — every PHI-badged field
    /// defaults to masked with an audit-logged reveal (§2.4).</summary>
    public bool HandlesPhi { get; set; }

    /// <summary>Domain relabelling of generic shell terms (e.g. record→claim).
    /// Keys are stable internal terms; values are what THIS tenant calls them.</summary>
    public Dictionary<string, string> Terminology { get; set; } = new();

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

/// <summary>Ordered from least to most sensitive; enforcement reads the
/// RANK, so a new intermediate tier slots in without touching checks.</summary>
public static class SensitivityTiers
{
    public const string Standard = "Standard";
    public const string Elevated = "Elevated";
    public const string HighSensitivity = "HighSensitivity";
    public const string Regulated = "Regulated";

    public static readonly string[] Ordered = [Standard, Elevated, HighSensitivity, Regulated];

    public static int Rank(string? tier)
    {
        var index = Array.FindIndex(Ordered, t => string.Equals(t, tier, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index; // unknown tier defaults to Standard
    }

    public static bool AtLeast(string? tier, string minimum) => Rank(tier) >= Rank(minimum);
}
