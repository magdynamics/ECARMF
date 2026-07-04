namespace ECARMF.Kernel.Domain.Scoring;

/// <summary>
/// The kernel's one generic scoring primitive. Trust, AssetReadiness,
/// DataConfidence, ControlEffectiveness, TreasuryEfficiency, … are all
/// ScoreRecords with different type tags — never separate mechanisms.
/// Immutable once written; score history is the learning signal.
/// </summary>
public class ScoreRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Entity type the score is about (e.g. Opportunity, Venture).</summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>Entity the score is about; defaults to the record id when the
    /// emitting rule names no subject field.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Package-defined score type tag (DataConfidence, Trust, …).</summary>
    public string ScoreType { get; set; } = string.Empty;

    public decimal Value { get; set; }

    /// <summary>Computed-by provenance: the rule and package version.</summary>
    public string? RuleId { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    /// <summary>Ties the score into its flywheel cycle in the audit log.</summary>
    public Guid CorrelationId { get; set; }

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
