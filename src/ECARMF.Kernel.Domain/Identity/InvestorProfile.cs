namespace ECARMF.Kernel.Domain.Identity;

/// <summary>
/// Investor identity gating (Batch 2, Refinement 10): a specialized profile
/// on top of a User. KYC/AML/accreditation are Pending|Verified|Rejected —
/// GATING DECISIONS through the accept/reject/escalate flow, never
/// calendar-based ComplianceRenewals. An unverified investor exists as an
/// identity but is not cleared to move capital.
/// </summary>
public class InvestorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The User this profile specializes.</summary>
    public string UserIdentifier { get; set; } = string.Empty;

    public string KycStatus { get; set; } = InvestorCheckStatuses.Pending;

    public string AmlStatus { get; set; } = InvestorCheckStatuses.Pending;

    public string AccreditationStatus { get; set; } = InvestorCheckStatuses.Pending;

    /// <summary>The Decision Intelligence outcome that gated onboarding —
    /// full traceability from investor status back to the decision record.</summary>
    public Guid? OnboardingDecisionId { get; set; }

    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Cleared only when every check is Verified.</summary>
    public bool IsCleared =>
        KycStatus == InvestorCheckStatuses.Verified
        && AmlStatus == InvestorCheckStatuses.Verified
        && AccreditationStatus == InvestorCheckStatuses.Verified;
}

public static class InvestorCheckStatuses
{
    public const string Pending = "Pending";
    public const string Verified = "Verified";
    public const string Rejected = "Rejected";

    public static readonly string[] All = { Pending, Verified, Rejected };
}

public static class InvestorChecks
{
    public const string Kyc = "kyc";
    public const string Aml = "aml";
    public const string Accreditation = "accreditation";
}
