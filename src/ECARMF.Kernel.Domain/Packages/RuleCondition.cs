namespace ECARMF.Kernel.Domain.Packages;

/// <summary>A single declarative comparison evaluated against an event
/// payload field at runtime.</summary>
public class RuleCondition
{
    /// <summary>Payload field the condition reads (e.g. "amount", "transactionType").</summary>
    public string Field { get; set; } = string.Empty;

    public ConditionOperator Operator { get; set; }

    /// <summary>Comparison value as declared in the manifest; coerced to the
    /// field's data type at evaluation time.</summary>
    public string Value { get; set; } = string.Empty;
}
