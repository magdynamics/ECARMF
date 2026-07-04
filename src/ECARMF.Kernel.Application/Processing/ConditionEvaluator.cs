using System.Globalization;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Processing;

/// <summary>
/// Evaluates a single declarative condition against an event payload.
/// Numeric comparison is used when both sides parse as decimal; otherwise
/// ordinal case-insensitive string comparison. A missing payload field never
/// matches — a control cannot fire on data it does not have.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Matches(RuleCondition condition, IReadOnlyDictionary<string, string> payload)
    {
        var actual = payload.FirstOrDefault(kv =>
            string.Equals(kv.Key, condition.Field, StringComparison.OrdinalIgnoreCase)).Value;

        if (actual is null)
        {
            return false;
        }

        var bothNumeric =
            decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber)
            & decimal.TryParse(condition.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber);

        if (bothNumeric)
        {
            return condition.Operator switch
            {
                ConditionOperator.Equals => actualNumber == expectedNumber,
                ConditionOperator.NotEquals => actualNumber != expectedNumber,
                ConditionOperator.GreaterThan => actualNumber > expectedNumber,
                ConditionOperator.LessThan => actualNumber < expectedNumber,
                ConditionOperator.GreaterOrEqual => actualNumber >= expectedNumber,
                ConditionOperator.LessOrEqual => actualNumber <= expectedNumber,
                ConditionOperator.Contains => actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        var comparison = string.Compare(actual, condition.Value, StringComparison.OrdinalIgnoreCase);
        return condition.Operator switch
        {
            ConditionOperator.Equals => comparison == 0,
            ConditionOperator.NotEquals => comparison != 0,
            ConditionOperator.GreaterThan => comparison > 0,
            ConditionOperator.LessThan => comparison < 0,
            ConditionOperator.GreaterOrEqual => comparison >= 0,
            ConditionOperator.LessOrEqual => comparison <= 0,
            ConditionOperator.Contains => actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
