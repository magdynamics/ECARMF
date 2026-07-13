using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

public interface IForecastingEngine
{
    /// <summary>Forecasts the next value of a score series (outputType
    /// ContinuousTrend). A forecast is a ScoreRecord with scoreType Forecast
    /// and provenance AIGenerated — not a new storage primitive. Returns null
    /// when history is too short.</summary>
    Task<ScoreRecord?> ForecastNextAsync(
        string tenantId, string subjectType, string subjectId, string sourceScoreType, CancellationToken ct = default);

    /// <summary>Classification forecast (Batch 3, Refinement 15, outputType
    /// Classification): a probability/categorical prediction — "probability
    /// this client churns in 90 days", "probability this employee resigns".
    /// Same ScoreRecord + AIGenerated discipline and the same
    /// AILearningFeedbackService trust loop as the trend path; only the method
    /// differs. The factors and their weights are package/tenant-defined
    /// domain logic passed in by the caller — the engine only does the
    /// weighted-sum + logistic arithmetic.</summary>
    Task<ScoreRecord?> ForecastClassificationAsync(
        string tenantId, string subjectType, string subjectId, string outcomeLabel,
        IReadOnlyList<WeightedFactor> factors, CancellationToken ct = default);
}

/// <summary>Forecast output modes (Batch 3, Refinement 15). Stored on the
/// forecast ScoreRecord's Metadata["outputType"], not as a schema field —
/// forecasts remain ScoreRecords.</summary>
public static class ForecastOutputTypes
{
    public const string ContinuousTrend = "ContinuousTrend";
    public const string Classification = "Classification";
}

/// <summary>
/// MVP linear-trend forecasting over the score history, using the kernel
/// StatisticalFunctionLibrary. Which method to use is package/config
/// territory later; the mechanism (interface + ScoreRecord output + accuracy
/// tracking via AILearningFeedbackService) does not change when a smarter
/// model replaces this one.
/// </summary>
public class ForecastingEngine : IForecastingEngine
{
    public const string Methodology = "linear-trend-v1";

    private readonly IScoreStore _scores;
    private readonly IAuditLog _audit;

    public ForecastingEngine(IScoreStore scores, IAuditLog audit)
    {
        _scores = scores;
        _audit = audit;
    }

    public async Task<ScoreRecord?> ForecastNextAsync(
        string tenantId, string subjectType, string subjectId, string sourceScoreType, CancellationToken ct = default)
    {
        var history = (await _scores.GetHistoryAsync(tenantId, subjectType, subjectId, ct))
            .Where(s => string.Equals(s.ScoreType, sourceScoreType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.ComputedAt)
            .Select(s => s.Value)
            .ToList();

        if (history.Count < 2)
        {
            return null;
        }

        var (slope, intercept) = StatisticalFunctionLibrary.LinearRegression(history);
        var predicted = slope * history.Count + intercept;
        var (lower, upper) = StatisticalFunctionLibrary.ConfidenceInterval95(history);

        var forecast = new ScoreRecord
        {
            TenantId = tenantId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ScoreType = "Forecast",
            Value = Math.Round(predicted, 6),
            RuleId = $"{Methodology}:{sourceScoreType}",
            Provenance = Provenance.AIGenerated,
            CorrelationId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string>
            {
                ["outputType"] = ForecastOutputTypes.ContinuousTrend,
                ["methodology"] = Methodology,
                ["sourceScoreType"] = sourceScoreType,
                ["forecastHorizon"] = "1-period",
                ["confidenceIntervalLower"] = lower.ToString(CultureInfo.InvariantCulture),
                ["confidenceIntervalUpper"] = upper.ToString(CultureInfo.InvariantCulture),
                ["sampleSize"] = history.Count.ToString(CultureInfo.InvariantCulture)
            }
        };

        await _scores.AppendAsync(forecast, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = forecast.CorrelationId,
            Category = AuditCategories.ScoreComputed,
            Actor = "system:flywheel",
            Summary = $"Forecast ({Methodology}) for {sourceScoreType} of {subjectType} '{subjectId}': {forecast.Value} (95% CI [{lower}, {upper}], n={history.Count}).",
            Detail = forecast.Metadata
        }, ct);

        return forecast;
    }

    public const string ClassificationMethodology = "logistic-weighted-v1";

    public async Task<ScoreRecord?> ForecastClassificationAsync(
        string tenantId, string subjectType, string subjectId, string outcomeLabel,
        IReadOnlyList<WeightedFactor> factors, CancellationToken ct = default)
    {
        if (factors is null || factors.Count == 0)
        {
            return null;
        }

        // Package-defined factors/weights → weighted score → logistic
        // probability. The kernel supplies only the arithmetic; the choice of
        // factors and the outcome being predicted are the package's.
        var rawScore = StatisticalFunctionLibrary.CalculateWeightedRiskScore(factors);
        var probability = Math.Round(StatisticalFunctionLibrary.Logistic(rawScore), 6);

        var forecast = new ScoreRecord
        {
            TenantId = tenantId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ScoreType = "Forecast",
            Value = probability,
            RuleId = $"{ClassificationMethodology}:{outcomeLabel}",
            Provenance = Provenance.AIGenerated,
            CorrelationId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string>
            {
                ["outputType"] = ForecastOutputTypes.Classification,
                ["methodology"] = ClassificationMethodology,
                ["outcomeLabel"] = outcomeLabel,
                ["rawWeightedScore"] = rawScore.ToString(CultureInfo.InvariantCulture),
                ["factorCount"] = factors.Count.ToString(CultureInfo.InvariantCulture)
            }
        };
        foreach (var factor in factors)
        {
            forecast.Metadata[$"factor:{factor.Name}"] =
                $"value={factor.Value.ToString(CultureInfo.InvariantCulture)} weight={factor.Weight.ToString(CultureInfo.InvariantCulture)}";
        }

        await _scores.AppendAsync(forecast, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = forecast.CorrelationId,
            Category = AuditCategories.ScoreComputed,
            Actor = "system:flywheel",
            Summary = $"Classification forecast ({ClassificationMethodology}) for {subjectType} '{subjectId}': " +
                      $"P({outcomeLabel}) = {probability} from {factors.Count} factor(s).",
            Detail = forecast.Metadata
        }, ct);

        return forecast;
    }
}
