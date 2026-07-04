namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// Declares an executable rule as pure metadata. The kernel evaluates the
/// conditions against event payloads at runtime; the rule carries everything
/// needed to explain an outcome (ECARMF-001 FND-0005: decisions shall be
/// explainable and traceable).
/// </summary>
public class RuleDeclaration
{
    public string RuleId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Event name this rule subscribes to (e.g. TransactionReceived).</summary>
    public string TriggerEvent { get; set; } = string.Empty;

    /// <summary>Execution order among rules for the same trigger event; lower runs first.</summary>
    public int Priority { get; set; }

    /// <summary>All conditions must hold for the rule to match (logical AND).</summary>
    public List<RuleCondition> Conditions { get; set; } = [];

    /// <summary>Package-defined outcome produced when the rule fires
    /// (e.g. Approved, Rejected, Flagged, Hold, Escalate, Accept). Empty for
    /// scoring-only rules: they emit their scores and processing continues
    /// to the next rule instead of deciding.</summary>
    public string OutcomeOnMatch { get; set; } = string.Empty;

    /// <summary>ScoreRecords emitted when this rule matches.</summary>
    public List<ScoreEmission> EmitScores { get; set; } = [];

    /// <summary>Human-readable explanation template recorded with the outcome,
    /// e.g. "Withdrawal of {amount} exceeds {threshold} and requires dual approval".</summary>
    public string ReasonTemplate { get; set; } = string.Empty;
}
