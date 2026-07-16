using ECARMF.Kernel.Application.Onboarding;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Tests;

/// <summary>The demo engine's record generators exist to make controls FIRE
/// and KPIs SCORE — a record that silently misses its rule produces an empty,
/// misleading demo.</summary>
public class DemoSeedingHelperTests
{
    [Theory]
    [InlineData(ConditionOperator.Equals, "DataAccess")]
    [InlineData(ConditionOperator.Contains, "cross")]
    [InlineData(ConditionOperator.GreaterThan, "100")]
    [InlineData(ConditionOperator.GreaterOrEqual, "4")]
    [InlineData(ConditionOperator.LessThan, "30")]
    [InlineData(ConditionOperator.LessOrEqual, "0")]
    [InlineData(ConditionOperator.NotEquals, "approved")]
    public void SatisfyingValue_actually_satisfies_the_condition(ConditionOperator op, string value)
    {
        var condition = new RuleCondition { Field = "f", Operator = op, Value = value };
        var produced = DemoSeedingService.SatisfyingValue(condition);

        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["f"] = produced };
        Assert.True(ConditionEvaluator.Matches(condition, payload),
            $"{op} '{value}' -> '{produced}' did not match its own condition");
    }

    [Fact]
    public void BuildRecord_satisfies_every_condition_of_a_multi_condition_rule()
    {
        var rule = new RuleDeclaration
        {
            RuleId = "FC-014",
            TriggerEvent = "RecordReceived",
            Conditions =
            [
                new RuleCondition { Field = "recordType", Operator = ConditionOperator.Equals, Value = "DataAccess" },
                new RuleCondition { Field = "crossTenant", Operator = ConditionOperator.Equals, Value = "true" },
                new RuleCondition { Field = "attempts", Operator = ConditionOperator.GreaterThan, Value = "3" }
            ]
        };

        var (recordType, payload) = DemoSeedingService.BuildRecord(rule);

        Assert.Equal("DataAccess", recordType);
        Assert.All(rule.Conditions, c => Assert.True(ConditionEvaluator.Matches(c, payload)));
    }

    [Fact]
    public void BuildKpiRecord_makes_the_formula_computable_and_populates_the_risk_token()
    {
        var kpi = new KPIDefinition
        {
            KpiId = "risk-index",
            TriggerRecordType = "RiskAssessment",
            Formula = "severityValue * likelihood",
            RiskType = "{domain}",
            SubjectField = "riskId",
            MetadataFields = ["severityValue", "likelihood"]
        };

        var payload = DemoSeedingService.BuildKpiRecord(kpi, i: 7);

        Assert.Equal("RiskAssessment", payload["recordType"]);
        // Every formula identifier resolves to a number.
        Assert.True(decimal.TryParse(payload["severityValue"], out var sev));
        Assert.True(decimal.TryParse(payload["likelihood"], out var like));
        // Severity/likelihood-style fields ride the 1..5 heatmap scale.
        Assert.InRange(sev, 1, 5);
        Assert.InRange(like, 1, 5);
        // The riskType token field is populated so the score lands on the heatmap.
        Assert.False(string.IsNullOrWhiteSpace(payload["domain"]));
        // A subject is present so the heatmap gets distinct points.
        Assert.False(string.IsNullOrWhiteSpace(payload["riskId"]));
    }

    [Fact]
    public void BuildEntityRecord_types_values_by_attribute_data_type()
    {
        var entity = new EntityDeclaration
        {
            EntityTypeName = "PayrollSummary",
            Attributes =
            [
                new AttributeDeclaration { Name = "headcount", DataType = "number" },
                new AttributeDeclaration { Name = "active", DataType = "boolean" },
                new AttributeDeclaration { Name = "periodEnd", DataType = "date" },
                new AttributeDeclaration { Name = "note", DataType = "string" }
            ]
        };

        var payload = DemoSeedingService.BuildEntityRecord(entity);

        Assert.Equal("PayrollSummary", payload["recordType"]);
        Assert.True(decimal.TryParse(payload["headcount"], out _));
        Assert.Equal("true", payload["active"]);
        Assert.True(DateTimeOffset.TryParse(payload["periodEnd"], out _));
        Assert.False(string.IsNullOrWhiteSpace(payload["note"]));
    }

    [Fact]
    public void Spread_backdates_within_the_period_window_never_into_the_future()
    {
        var now = DateTimeOffset.UtcNow;
        for (var seq = 0; seq < 300; seq++)
        {
            var at = DemoSeedingService.Spread(seq);
            Assert.True(at <= now.AddMinutes(1), $"seq {seq} produced a future timestamp");
            Assert.True(at >= now.AddDays(-120), $"seq {seq} fell outside the ~4-month window");
        }
    }

    [Fact]
    public void CaseIdFor_leaves_every_fourth_record_uncased_and_rotates_the_rest()
    {
        string[] cases = ["a", "b", "c"];
        Assert.Null(DemoSeedingService.CaseIdFor(0, cases));
        Assert.Null(DemoSeedingService.CaseIdFor(4, cases));
        Assert.Equal("b", DemoSeedingService.CaseIdFor(1, cases));
        Assert.Equal("c", DemoSeedingService.CaseIdFor(2, cases));
        Assert.Equal("a", DemoSeedingService.CaseIdFor(3, cases));
        // No cases at all -> everything uncased, never a crash.
        Assert.Null(DemoSeedingService.CaseIdFor(1, []));
    }
}
