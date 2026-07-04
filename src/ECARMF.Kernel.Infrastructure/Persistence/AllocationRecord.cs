namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for an AllocationRecommendation. The ranked
/// alternatives, assumptions, risk factors, and supporting score ids are
/// stored as JSON documents.</summary>
public class AllocationRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string TargetReference { get; set; } = string.Empty;

    public string? TargetAssetClass { get; set; }

    public decimal RecommendedAmount { get; set; }

    public string? TargetInstitution { get; set; }

    public string? TargetJurisdiction { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string Reasoning { get; set; } = string.Empty;

    public string AssumptionsJson { get; set; } = "[]";

    public string RiskFactorsJson { get; set; } = "[]";

    public string AlternativesJson { get; set; } = "[]";

    public string SupportingScoreIdsJson { get; set; } = "[]";

    public string Tier { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public decimal? ModifiedAmount { get; set; }
}
