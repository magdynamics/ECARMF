namespace ECARMF.Kernel.Domain.Capital;

public static class CapitalFlowDirections
{
    public const string Outbound = "Outbound";
    public const string Inbound = "Inbound";
}

/// <summary>Decision authority tiers. The AI never self-approves its own
/// escalated outcomes — the same rule as RequireDualApproval, generalized
/// to allocation decisions.</summary>
public enum AutonomyTier
{
    /// <summary>AI executes directly: below value threshold, high confidence,
    /// pre-approved venture/institution/jurisdiction.</summary>
    Autonomous,

    /// <summary>AI produces a ranked recommendation; a human approves,
    /// modifies, or rejects.</summary>
    RecommendOnly,

    /// <summary>AI flags and stops: new institution/jurisdiction, low
    /// confidence, high value, or conflicting signals.</summary>
    Escalated
}

/// <summary>A ranked alternative the engine considered and did not pick.</summary>
public class AllocationAlternative
{
    public string TargetReference { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// One entity for capital moving in either direction (Batch 1, Refinement
/// 2): an outbound treasury sweep and an inbound construction draw or
/// investor capital call are the same shape with Direction flipped.
/// Reasoning, confidence, assumptions, and risk factors are required on
/// every flow, both directions, no exceptions.
/// </summary>
public class CapitalFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Outbound | Inbound.</summary>
    public string Direction { get; set; } = CapitalFlowDirections.Outbound;

    /// <summary>Lender/investor identity the capital comes FROM (Inbound).</summary>
    public string? SourceId { get; set; }

    /// <summary>For Inbound draws tied to verified progress (e.g. a
    /// construction milestone); null for most Outbound flows.</summary>
    public string? MilestoneReference { get; set; }

    /// <summary>Where: the venture/opportunity the capital would go to.</summary>
    public string TargetReference { get; set; } = string.Empty;

    public string? TargetAssetClass { get; set; }

    /// <summary>How much.</summary>
    public decimal Amount { get; set; }

    /// <summary>Which bank/custodian/broker it would route through.</summary>
    public string? TargetInstitution { get; set; }

    /// <summary>Which legal/tax structure.</summary>
    public string? TargetJurisdiction { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string Reasoning { get; set; } = string.Empty;

    public List<string> Assumptions { get; set; } = [];

    public List<string> RiskFactors { get; set; } = [];

    /// <summary>Ranked list, not just the top pick — a reviewer sees what
    /// was rejected and why.</summary>
    public List<AllocationAlternative> AlternativesConsidered { get; set; } = [];

    /// <summary>ScoreRecord ids the reasoning is grounded in (AssetReadiness,
    /// DataConfidence, OKRAttainment, ...).</summary>
    public List<Guid> SupportingScoreRecordIds { get; set; } = [];

    public AutonomyTier Tier { get; set; }

    /// <summary>Pending | AutoExecuted | Approved | Modified | Rejected.</summary>
    public string Status { get; set; } = "Pending";

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public decimal? ModifiedAmount { get; set; }
}

/// <summary>Autonomy thresholds (kernel defaults; a smarter policy can ship
/// as configuration later without changing the mechanism).</summary>
public sealed record AutonomyPolicy(
    decimal AutonomousMaxAmount,
    decimal AutonomousMinConfidence,
    decimal EscalateAboveAmount,
    decimal EscalateBelowConfidence)
{
    public static readonly AutonomyPolicy Default = new(50_000m, 0.8m, 1_000_000m, 0.5m);

    public AutonomyTier Classify(decimal amount, decimal confidence, bool knownTarget)
    {
        if (amount > EscalateAboveAmount || confidence < EscalateBelowConfidence || !knownTarget)
        {
            return AutonomyTier.Escalated;
        }

        if (amount <= AutonomousMaxAmount && confidence >= AutonomousMinConfidence)
        {
            return AutonomyTier.Autonomous;
        }

        return AutonomyTier.RecommendOnly;
    }
}
