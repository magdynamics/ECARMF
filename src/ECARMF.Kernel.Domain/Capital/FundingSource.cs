namespace ECARMF.Kernel.Domain.Capital;

/// <summary>
/// Capital flowing INTO a project against verified progress or contractual
/// terms (Rosetta Requirement 4) — the reverse direction from CapitalFlow's
/// outbound allocations. One mechanism covers both shapes from the start:
/// a construction lender (Debt — draws against milestones) and investors
/// (Equity — capital calls, contributions, and eventually distributions).
/// </summary>
public class FundingSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable slug, e.g. "construction-lender", "investor-alpha".</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>The project unit this capital funds.</summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>Debt | Equity.</summary>
    public string Kind { get; set; } = FundingSourceKinds.Debt;

    /// <summary>Lender or investor name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>For Equity sources: the gated investor identity behind this
    /// capital (Batch 2, Refinement 10) — links the money to the KYC/AML/
    /// accreditation-verified User once investor details arrive. Null while
    /// the investor is a placeholder or for Debt sources.</summary>
    public string? InvestorUserId { get; set; }

    public string? Institution { get; set; }

    /// <summary>Total loan commitment / investor commitment.</summary>
    public decimal? CommitmentAmount { get; set; }

    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class FundingSourceKinds
{
    public const string Debt = "Debt";
    public const string Equity = "Equity";
}

/// <summary>
/// One movement (or requested movement) of capital from a FundingSource:
/// a lender draw claimed against a milestone, an investor capital call or
/// contribution, or a distribution back out once the project stabilizes.
/// Requested by anyone with data-entry rights; approved only by a human
/// with dual-approval authority; every step audited — full traceability on
/// what was claimed, what was verified, and what was approved.
/// </summary>
public class FundingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public Guid FundingSourceId { get; set; }

    /// <summary>Draw | CapitalCall | Contribution | Distribution.</summary>
    public string EventType { get; set; } = FundingEventTypes.Draw;

    /// <summary>For lender draws: the construction milestone the request is
    /// claimed against (e.g. "foundation-complete").</summary>
    public string? MilestoneReference { get; set; }

    /// <summary>% complete claimed for the milestone (0..1).</summary>
    public decimal? PercentCompleteClaimed { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Supporting documentation pointer (library document id,
    /// draw package reference, subscription doc...).</summary>
    public string? DocumentationReference { get; set; }

    /// <summary>What was checked against operational data (e.g. SiteView
    /// completion events) before the human decision.</summary>
    public string? VerificationNote { get; set; }

    /// <summary>Requested | Approved | Rejected | Disbursed.</summary>
    public string Status { get; set; } = FundingEventStatuses.Requested;

    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public DateTimeOffset? DisbursedAt { get; set; }
}

public static class FundingEventTypes
{
    public const string Draw = "Draw";
    public const string CapitalCall = "CapitalCall";
    public const string Contribution = "Contribution";
    public const string Distribution = "Distribution";

    public static readonly string[] All = { Draw, CapitalCall, Contribution, Distribution };
}

public static class FundingEventStatuses
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Disbursed = "Disbursed";
}
