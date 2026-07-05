namespace ECARMF.Kernel.Domain.Treasury;

/// <summary>
/// A bank account under treasury sweep management (Universal Dental
/// Requirement 8). Operating accounts sweep their overage above an
/// APPROVED threshold to the destination account autonomously; the
/// threshold itself is continuously re-proposed by the AI treasury
/// function and only takes effect after human approval (Recommend-Only).
/// Payroll accounts are never swept — they get a high-balance alert
/// instead, because sweeping around pay cycles creates payday shortfalls.
/// </summary>
public class SweepAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable slug, e.g. "oak-lawn-operating".</summary>
    public string AccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Org unit this account belongs to (e.g. the practice).</summary>
    public string? UnitId { get; set; }

    public string Institution { get; set; } = string.Empty;

    /// <summary>Operating | Payroll. Only Operating accounts sweep.</summary>
    public string Kind { get; set; } = SweepAccountKinds.Operating;

    /// <summary>Where the overage goes (e.g. "corporate-operating").
    /// Same-institution destinations keep sweeps in the Autonomous tier.</summary>
    public string? DestinationAccountId { get; set; }

    /// <summary>The standing threshold sweeps execute against. Only a human
    /// approval sets this — never the recalculation directly.</summary>
    public decimal? ApprovedThreshold { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>The AI treasury function's latest proposal, awaiting review.
    /// Never silently applied.</summary>
    public decimal? ProposedThreshold { get; set; }

    public DateTimeOffset? ProposedAt { get; set; }

    public string? ProposalReasoning { get; set; }

    public bool Enabled { get; set; } = true;

    public decimal? LastObservedBalance { get; set; }

    public DateTimeOffset? LastObservedAt { get; set; }

    public DateTimeOffset? LastSweepAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class SweepAccountKinds
{
    public const string Operating = "Operating";
    public const string Payroll = "Payroll";
}
