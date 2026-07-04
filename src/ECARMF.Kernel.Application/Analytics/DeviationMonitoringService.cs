using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

/// <summary>Persistence port for deviation alerts.</summary>
public interface IDeviationStore
{
    Task AddAsync(DeviationAlert alert, CancellationToken ct = default);
    Task<DeviationAlert?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task UpdateAsync(DeviationAlert alert, CancellationToken ct = default);
    Task<IReadOnlyList<DeviationAlert>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);
}

public interface IDeviationMonitor
{
    /// <summary>Compares a freshly computed KPIActual against its baseline —
    /// the KPI target when available, otherwise the latest Forecast for the
    /// same subject — and raises a DeviationAlert when the gap exceeds the
    /// threshold. Just another check in the same processing pass.</summary>
    Task CheckAsync(string tenantId, ScoreRecord kpiActual, decimal? targetValue, Guid correlationId, CancellationToken ct = default);

    /// <summary>Missing data is itself a deviation: raises an alert for every
    /// subject whose KPIActual stream has gone silent longer than maxAge.</summary>
    Task<int> CheckMissingDataAsync(string tenantId, TimeSpan maxAge, CancellationToken ct = default);
}

public class DeviationMonitoringService : IDeviationMonitor
{
    /// <summary>Relative gap that triggers an alert; 2x = Critical.</summary>
    public const decimal DefaultThreshold = 0.20m;

    private readonly IDeviationStore _alerts;
    private readonly IScoreStore _scores;
    private readonly IAuditLog _audit;

    public DeviationMonitoringService(IDeviationStore alerts, IScoreStore scores, IAuditLog audit)
    {
        _alerts = alerts;
        _scores = scores;
        _audit = audit;
    }

    public async Task CheckAsync(
        string tenantId, ScoreRecord kpiActual, decimal? targetValue, Guid correlationId, CancellationToken ct = default)
    {
        decimal expected;
        string source;

        if (targetValue is { } target && target != 0)
        {
            expected = target;
            source = "Target";
        }
        else
        {
            var forecast = (await _scores.GetHistoryAsync(tenantId, kpiActual.SubjectType, kpiActual.SubjectId, ct))
                .Where(s => s.ScoreType == "Forecast")
                .OrderByDescending(s => s.ComputedAt)
                .FirstOrDefault();
            if (forecast is null || forecast.Value == 0)
            {
                return; // no baseline configured — nothing to deviate from
            }
            expected = forecast.Value;
            source = "Forecast";
        }

        var variance = (kpiActual.Value - expected) / expected;
        if (Math.Abs(variance) <= DefaultThreshold)
        {
            return;
        }

        var severity = Math.Abs(variance) > DefaultThreshold * 2 ? "Critical" : "Warning";
        var alert = new DeviationAlert
        {
            TenantId = tenantId,
            EntityReference = kpiActual.SubjectId,
            MetricType = kpiActual.ScoreType,
            ActualValue = kpiActual.Value,
            ExpectedValue = expected,
            ExpectedValueSource = source,
            VarianceMagnitude = Math.Round(variance, 4),
            ThresholdBreached = DefaultThreshold,
            Severity = severity,
            CorrelationId = correlationId
        };

        await _alerts.AddAsync(alert, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = AuditCategories.DeviationDetected,
            Actor = "system:flywheel",
            Summary = $"{severity} deviation on '{kpiActual.SubjectId}': actual {kpiActual.Value} vs {source.ToLowerInvariant()} {expected} ({variance:P1}).",
            Detail = new Dictionary<string, string>
            {
                ["entityReference"] = alert.EntityReference,
                ["actual"] = alert.ActualValue.ToString(CultureInfo.InvariantCulture),
                ["expected"] = alert.ExpectedValue.ToString(CultureInfo.InvariantCulture),
                ["expectedValueSource"] = source,
                ["variance"] = alert.VarianceMagnitude.ToString(CultureInfo.InvariantCulture),
                ["severity"] = severity
            }
        }, ct);
    }

    public async Task<int> CheckMissingDataAsync(string tenantId, TimeSpan maxAge, CancellationToken ct = default)
    {
        var recent = await _scores.GetRecentAsync(tenantId, 500, "KPIActual", ct);
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var raised = 0;

        foreach (var stale in recent
            .GroupBy(s => s.SubjectId)
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .Where(latest => latest.ComputedAt < cutoff))
        {
            var alert = new DeviationAlert
            {
                TenantId = tenantId,
                EntityReference = stale.SubjectId,
                MetricType = "KPIActual",
                ActualValue = 0,
                ExpectedValue = stale.Value,
                ExpectedValueSource = "MissingData",
                VarianceMagnitude = -1,
                ThresholdBreached = 0,
                Severity = "Warning",
                CorrelationId = stale.CorrelationId
            };

            await _alerts.AddAsync(alert, ct);
            await _audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = stale.CorrelationId,
                Category = AuditCategories.DeviationDetected,
                Actor = "system:flywheel",
                Summary = $"Missing data: no KPIActual for '{stale.SubjectId}' since {stale.ComputedAt:O} (max age {maxAge}). Silence is a failure mode.",
                Detail = new Dictionary<string, string>
                {
                    ["entityReference"] = stale.SubjectId,
                    ["lastSeen"] = stale.ComputedAt.ToString("O"),
                    ["expectedValueSource"] = "MissingData"
                }
            }, ct);
            raised++;
        }

        return raised;
    }
}
