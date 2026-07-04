using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

public interface IForecastingEngine
{
    /// <summary>Forecasts the next value of a score series. A ForecastRecord
    /// is a ScoreRecord with scoreType Forecast and provenance AIGenerated —
    /// not a new storage primitive. Returns null when history is too short.</summary>
    Task<ScoreRecord?> ForecastNextAsync(
        string tenantId, string subjectType, string subjectId, string sourceScoreType, CancellationToken ct = default);
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
}
