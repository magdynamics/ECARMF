namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// Declares a ScoreRecord a rule emits when it matches — pure metadata.
/// Value is a literal number or a {field} token resolved from the event
/// payload at runtime.
/// </summary>
public class ScoreEmission
{
    /// <summary>Package-defined score type (DataConfidence, AssetReadiness, Trust, …).</summary>
    public string ScoreType { get; set; } = string.Empty;

    /// <summary>Literal number ("0.85") or payload token ("{reliabilityRating}").</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Entity type the score is about; defaults to the record type.</summary>
    public string? SubjectType { get; set; }

    /// <summary>Payload field holding the subject entity id; defaults to the record id.</summary>
    public string? SubjectIdField { get; set; }
}
