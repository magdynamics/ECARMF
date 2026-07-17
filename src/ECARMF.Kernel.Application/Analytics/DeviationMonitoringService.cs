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
    /// threshold. When <paramref name="direction"/> is supplied ("higher" |
    /// "lower"), only UNFAVORABLE deviations alert: beating a target is
    /// performance, not a problem. A target of exactly 0 with a known
    /// direction is an absolute line (e.g. "zero schedule slip") — any
    /// unfavorable actual breaches it. Null direction keeps the legacy
    /// direction-agnostic behavior.</summary>
    Task CheckAsync(string tenantId, ScoreRecord kpiActual, decimal? targetValue, Guid correlationId, string? direction = null, CancellationToken ct = default);

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
        string tenantId, ScoreRecord kpiActual, decimal? targetValue, Guid correlationId,
        string? direction = null, CancellationToken ct = default)
    {
        decimal expected;
        string source;
        var hasDirection = direction is not null;

        if (targetValue is { } target && (target != 0 || hasDirection))
        {
            // A target of exactly 0 is meaningful ONLY with a direction —
            // "zero slip days", "zero incidents" — otherwise it stays the
            // legacy no-baseline case (fall through to forecast).
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

        // Direction-aware: beating the target is performance, not a problem.
        // (Rosetta/MagDynamics finding: training hours ABOVE target and
        // shrinkage far BELOW target were flagged Critical.)
        var lowerIsBetter = string.Equals(direction, "lower", StringComparison.OrdinalIgnoreCase);
        var higherIsBetter = string.Equals(direction, "higher", StringComparison.OrdinalIgnoreCase);
        if (higherIsBetter && kpiActual.Value >= expected) return;
        if (lowerIsBetter && kpiActual.Value <= expected) return;

        decimal variance;
        string severity;
        if (expected == 0)
        {
            // Absolute line ("zero slip"): any unfavorable actual breaches
            // it; there is no denominator, so magnitude IS the actual and
            // the breach is Critical — the tenant declared zero tolerance.
            variance = kpiActual.Value;
            severity = "Critical";
        }
        else
        {
            variance = (kpiActual.Value - expected) / expected;
            if (Math.Abs(variance) <= DefaultThreshold)
            {
                return;
            }
            severity = Math.Abs(variance) > DefaultThreshold * 2 ? "Critical" : "Warning";
        }
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
            UnitRef = kpiActual.UnitRef, // a unit's KPI deviating is that unit's alert
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
                UnitRef = stale.UnitRef,
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
