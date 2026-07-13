using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Performance;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Relationships;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Batch 3 refinements: generic EntityRelationship (R13), the
/// CompositeHealth rollup pattern (R14), Classification forecasting (R15),
/// the weighted multi-factor risk function (R16), and the derived recurrence
/// pattern on RenewalCommitment (R17).</summary>
public class Batch3RefinementTests
{
    private const string Tenant = "batch3";

    [Fact]
    public async Task Dynamic_kpi_risk_type_token_lets_one_kpi_tag_per_record_category()
    {
        // Tenant-10-driven refinement: a single risk-register KPI over ONE
        // record type stamps per-record risk categories via a {token}, instead
        // of a separate record type + KPI per category.
        var registries = new TenantRegistryProvider();
        var scores = new InMemoryScoreStore();
        var evaluator = new PerformanceEvaluationService(registries, scores, new InMemoryAuditLog());

        registries.GetFor(Tenant).PerformanceFrameworks.Register(new PerformanceFrameworkDeclaration
        {
            FrameworkId = "risk-register-v1", Name = "Risk Register", Industry = "Any",
            Kpis =
            [
                new KPIDefinition
                {
                    KpiId = "risk-index", Formula = "likelihood * impact",
                    TriggerRecordType = "RiskAssessment", SubjectField = "area",
                    SubjectType = "Risk", RiskType = "{category}", // resolved per record
                    TargetValue = 20, Direction = "lower"
                }
            ]
        }, "ecarmf.ai-tcel", "1.0.0");

        await evaluator.EvaluateAsync(new KernelEvent(Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "RiskAssessment", ["area"] = "patient-portal",
                ["category"] = "PHIBreach", ["likelihood"] = "4", ["impact"] = "5"
            }, DateTimeOffset.UtcNow));

        await evaluator.EvaluateAsync(new KernelEvent(Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = "RiskAssessment", ["area"] = "payer-aetna",
                ["category"] = "ClaimDenial", ["likelihood"] = "3", ["impact"] = "3"
            }, DateTimeOffset.UtcNow));

        var actuals = scores.Items.Where(s => s.ScoreType == "KPIActual").ToList();
        // Same KPI, one record type — different riskType per record's category.
        Assert.Equal("PHIBreach", actuals.Single(s => s.SubjectId == "risk-index@patient-portal").RiskType);
        Assert.Equal("ClaimDenial", actuals.Single(s => s.SubjectId == "risk-index@payer-aetna").RiskType);
    }

    [Fact]
    public void R16_weighted_risk_score_is_the_weight_normalized_mean()
    {
        var factors = new[]
        {
            new WeightedFactor("Likelihood", 0.8m, 3m),
            new WeightedFactor("FinancialImpact", 0.4m, 1m)
        };

        // (0.8*3 + 0.4*1) / (3+1) = 2.8 / 4 = 0.7
        Assert.Equal(0.7m, StatisticalFunctionLibrary.CalculateWeightedRiskScore(factors));
        // Degenerate inputs never divide by zero.
        Assert.Equal(0m, StatisticalFunctionLibrary.CalculateWeightedRiskScore(Array.Empty<WeightedFactor>()));
        Assert.Equal(0m, StatisticalFunctionLibrary.CalculateWeightedRiskScore(
            new[] { new WeightedFactor("x", 5m, 0m) }));
    }

    [Fact]
    public void R16_multiplicative_risk_score_is_the_product_of_factors()
    {
        // Tenant-10's model: Likelihood x Business Impact x ... (plain product).
        var factors = new[]
        {
            new WeightedFactor("Likelihood", 0.5m, 1m),
            new WeightedFactor("BusinessImpact", 0.8m, 1m),
            new WeightedFactor("AIConfidence", 0.9m, 1m)
        };
        // 0.5 * 0.8 * 0.9 = 0.36
        Assert.Equal(0.36m, StatisticalFunctionLibrary.CalculateMultiplicativeRiskScore(factors));

        // A zero factor legitimately zeroes the product; empty is 0.
        Assert.Equal(0m, StatisticalFunctionLibrary.CalculateMultiplicativeRiskScore(
            new[] { new WeightedFactor("x", 0m, 1m), new WeightedFactor("y", 0.9m, 1m) }));
        Assert.Equal(0m, StatisticalFunctionLibrary.CalculateMultiplicativeRiskScore(Array.Empty<WeightedFactor>()));

        // A weight applies value^weight: 0.5^2 * 1 = 0.25.
        Assert.Equal(0.25m, StatisticalFunctionLibrary.CalculateMultiplicativeRiskScore(
            new[] { new WeightedFactor("emphasised", 0.5m, 2m), new WeightedFactor("flat", 1m, 1m) }));
    }

    [Fact]
    public async Task R13_R14_composite_health_rolls_up_child_scores_by_edge_weight()
    {
        var relationships = new InMemoryEntityRelationshipStore();
        var scores = new InMemoryScoreStore();
        var audit = new InMemoryAuditLog();
        var service = new CompositeHealthService(relationships, scores, audit);

        // Two children, each with a HealthScore, wired to the parent with weights.
        await scores.AppendAsync(new ScoreRecord
        {
            TenantId = Tenant, SubjectType = "Process", SubjectId = "coding-accuracy",
            ScoreType = "HealthScore", Value = 0.9m, CorrelationId = Guid.NewGuid(),
            ComputedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        await scores.AppendAsync(new ScoreRecord
        {
            TenantId = Tenant, SubjectType = "Process", SubjectId = "ar-aging",
            ScoreType = "HealthScore", Value = 0.5m, CorrelationId = Guid.NewGuid(),
            ComputedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        foreach (var (child, weight) in new[] { ("coding-accuracy", 3m), ("ar-aging", 1m) })
        {
            await relationships.AddAsync(new EntityRelationship
            {
                TenantId = Tenant, SubjectType = "RevenueCycle", SubjectId = "practice-1",
                RelatedType = "Process", RelatedId = child,
                RelationshipType = RelationshipTypes.RollsUpInto, Strength = weight
            });
        }

        var composite = await service.ComputeAsync(Tenant, "RevenueCycle", "practice-1", "HealthScore");

        Assert.NotNull(composite);
        Assert.Equal("CompositeHealth", composite!.ScoreType);
        // (0.9*3 + 0.5*1) / 4 = 0.8
        Assert.Equal(0.8m, composite.Value);
        Assert.Equal("AIGenerated", composite.Provenance);
        Assert.Equal("2", composite.Metadata["childCount"]);
        Assert.Contains(audit.Items, a => a.Category == "CompositeHealthComputed");
    }

    [Fact]
    public async Task R14_composite_returns_null_when_no_rollup_edges_have_scored_children()
    {
        var relationships = new InMemoryEntityRelationshipStore();
        var scores = new InMemoryScoreStore();
        var service = new CompositeHealthService(relationships, scores, new InMemoryAuditLog());

        // Edge exists but the child has no score yet.
        await relationships.AddAsync(new EntityRelationship
        {
            TenantId = Tenant, SubjectType = "RevenueCycle", SubjectId = "practice-2",
            RelatedType = "Process", RelatedId = "denials",
            RelationshipType = RelationshipTypes.RollsUpInto, Strength = 1m
        });

        Assert.Null(await service.ComputeAsync(Tenant, "RevenueCycle", "practice-2", "HealthScore"));
    }

    [Fact]
    public async Task R14_non_rollup_edges_are_ignored_by_the_composite()
    {
        var relationships = new InMemoryEntityRelationshipStore();
        var scores = new InMemoryScoreStore();
        var service = new CompositeHealthService(relationships, scores, new InMemoryAuditLog());

        await scores.AppendAsync(new ScoreRecord
        {
            TenantId = Tenant, SubjectType = "Process", SubjectId = "churn",
            ScoreType = "HealthScore", Value = 0.3m, CorrelationId = Guid.NewGuid()
        });
        // A Correlates edge is NOT a rollup child — it must not contribute.
        await relationships.AddAsync(new EntityRelationship
        {
            TenantId = Tenant, SubjectType = "RevenueCycle", SubjectId = "practice-3",
            RelatedType = "Process", RelatedId = "churn",
            RelationshipType = RelationshipTypes.Correlates, Strength = 1m
        });

        Assert.Null(await service.ComputeAsync(Tenant, "RevenueCycle", "practice-3", "HealthScore"));
    }

    [Fact]
    public async Task R15_classification_forecast_is_a_probability_scorerecord_tagged_classification()
    {
        var scores = new InMemoryScoreStore();
        var engine = new ForecastingEngine(scores, new InMemoryAuditLog());

        var factors = new[]
        {
            new WeightedFactor("SupportTickets", 1.2m, 2m),
            new WeightedFactor("UsageDecline", 0.9m, 1m)
        };

        var forecast = await engine.ForecastClassificationAsync(
            Tenant, "Client", "acme", "churn-90d", factors);

        Assert.NotNull(forecast);
        Assert.Equal("Forecast", forecast!.ScoreType);
        Assert.Equal(ForecastOutputTypes.Classification, forecast.Metadata["outputType"]);
        Assert.Equal("churn-90d", forecast.Metadata["outcomeLabel"]);
        Assert.Equal("AIGenerated", forecast.Provenance);
        // Logistic output is a probability in (0,1).
        Assert.InRange(forecast.Value, 0m, 1m);
    }

    [Fact]
    public async Task R15_trend_forecast_is_tagged_continuous_trend()
    {
        var scores = new InMemoryScoreStore();
        var engine = new ForecastingEngine(scores, new InMemoryAuditLog());
        for (var i = 0; i < 3; i++)
        {
            await scores.AppendAsync(new ScoreRecord
            {
                TenantId = Tenant, SubjectType = "Venture", SubjectId = "v1",
                ScoreType = "Trust", Value = 0.5m + i * 0.1m, CorrelationId = Guid.NewGuid(),
                ComputedAt = DateTimeOffset.UtcNow.AddMinutes(i)
            });
        }

        var forecast = await engine.ForecastNextAsync(Tenant, "Venture", "v1", "Trust");

        Assert.NotNull(forecast);
        Assert.Equal(ForecastOutputTypes.ContinuousTrend, forecast!.Metadata["outputType"]);
    }

    [Theory]
    [InlineData(null, "OneTime")]
    [InlineData(0, "OneTime")]
    [InlineData(1, "Monthly")]
    [InlineData(3, "Quarterly")]
    [InlineData(12, "Annual")]
    [InlineData(6, "Every 6 months")]
    public void R17_recurrence_pattern_is_derived_from_recurrence_months(int? months, string expected)
    {
        var commitment = new RenewalCommitment { RecurrenceMonths = months };
        Assert.Equal(expected, commitment.RecurrencePattern);
        Assert.Equal(expected, RenewalRecurrence.Describe(months));
    }
}
