using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Performance;

public interface IPerformanceEvaluator
{
    /// <summary>Evaluates every active framework's KPIs against an incoming
    /// record event, emitting KPIActual / KPIVariance / OKRAttainment
    /// ScoreRecords. Runs inside the same event-processing pass — KPI
    /// calculation is a rule, not a new execution mechanism.</summary>
    Task EvaluateAsync(KernelEvent kernelEvent, CancellationToken ct = default);
}

/// <summary>Recommends frameworks for an industry. MVP: classification
/// lookup against registered frameworks; the interface supports a
/// smarter/AI-driven implementation later without kernel changes.</summary>
public interface IFrameworkRecommender
{
    IReadOnlyList<Registered<PerformanceFrameworkDeclaration>> Recommend(string tenantId, string industry);
}

public class PerformanceEvaluationService : IPerformanceEvaluator, IFrameworkRecommender
{
    private readonly ITenantRegistryProvider _registries;
    private readonly IScoreStore _scores;
    private readonly IAuditLog _audit;
    private readonly Analytics.IDeviationMonitor? _deviations;
    private readonly Analytics.IBenchmarkMonitor? _benchmarks;

    public PerformanceEvaluationService(
        ITenantRegistryProvider registries, IScoreStore scores, IAuditLog audit,
        Analytics.IDeviationMonitor? deviations = null,
        Analytics.IBenchmarkMonitor? benchmarks = null)
    {
        _registries = registries;
        _scores = scores;
        _audit = audit;
        _deviations = deviations;
        _benchmarks = benchmarks;
    }

    public IReadOnlyList<Registered<PerformanceFrameworkDeclaration>> Recommend(string tenantId, string industry)
    {
        return _registries.GetFor(tenantId).PerformanceFrameworks.GetAll()
            .Where(f => f.Declaration.Industry.Contains(industry, StringComparison.OrdinalIgnoreCase)
                     || industry.Contains(f.Declaration.Industry, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task EvaluateAsync(KernelEvent kernelEvent, CancellationToken ct = default)
    {
        var recordType = kernelEvent.Payload.FirstOrDefault(kv =>
            string.Equals(kv.Key, "recordType", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(recordType))
        {
            return;
        }

        var frameworks = _registries.GetFor(kernelEvent.TenantId).PerformanceFrameworks.GetAll();

        foreach (var framework in frameworks)
        {
            // KPI actuals computed from this record's payload.
            var actuals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var kpi in framework.Declaration.Kpis)
            {
                if (!string.Equals(kpi.TriggerRecordType, recordType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!KpiFormulaEvaluator.TryEvaluate(kpi.Formula, kernelEvent.Payload, out var value))
                {
                    continue; // fields not present on this record — not this KPI's event
                }

                actuals[kpi.KpiId] = value;
                var subject = ResolveSubject(kernelEvent, kpi.SubjectField);

                var actualScore = await EmitAsync(kernelEvent, framework, "KPIActual", $"{kpi.KpiId}@{subject}", value, ct);

                // Deviation monitoring runs in the same pass: actual vs the
                // KPI target (or the latest forecast when no target exists).
                if (_deviations is not null)
                {
                    await _deviations.CheckAsync(kernelEvent.TenantId, actualScore, kpi.TargetValue, kernelEvent.CorrelationId, ct);
                }

                // Tenant-set expectations (e.g. "GP% must stay >= 25%") watch KPIs too.
                if (_benchmarks is not null)
                {
                    await _benchmarks.CheckScoreAsync(actualScore, ct);
                }

                if (kpi.TargetValue is { } target && target != 0)
                {
                    var variance = (value - target) / target;
                    await EmitAsync(kernelEvent, framework, "KPIVariance", $"{kpi.KpiId}@{subject}", variance, ct);
                }
            }

            if (actuals.Count == 0)
            {
                continue;
            }

            // OKR attainment: average capped actual/target across the key
            // results whose KPIs were computed in this pass.
            foreach (var okr in framework.Declaration.Okrs)
            {
                var attainments = new List<decimal>();
                foreach (var kr in okr.KeyResults)
                {
                    if (!actuals.TryGetValue(kr.KpiId, out var actual) || kr.TargetValue == 0)
                    {
                        continue;
                    }

                    var kpi = framework.Declaration.Kpis.First(k =>
                        string.Equals(k.KpiId, kr.KpiId, StringComparison.OrdinalIgnoreCase));
                    var ratio = kpi.Direction.Equals("lower", StringComparison.OrdinalIgnoreCase)
                        ? (actual == 0 ? 1m : Math.Min(1m, kr.TargetValue / actual))
                        : Math.Min(1m, actual / kr.TargetValue);
                    attainments.Add(Math.Max(0m, ratio));
                }

                if (attainments.Count > 0)
                {
                    var subject = ResolveSubject(kernelEvent, framework.Declaration.Kpis[0].SubjectField);
                    await EmitAsync(kernelEvent, framework, "OKRAttainment",
                        $"{okr.OkrId}@{subject}", Math.Round(attainments.Average(), 4), ct);
                }
            }
        }
    }

    private static string ResolveSubject(KernelEvent kernelEvent, string subjectField)
    {
        if (!string.IsNullOrWhiteSpace(subjectField))
        {
            var value = kernelEvent.Payload.FirstOrDefault(kv =>
                string.Equals(kv.Key, subjectField, StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return kernelEvent.CorrelationId.ToString();
    }

    private async Task<ScoreRecord> EmitAsync(
        KernelEvent kernelEvent,
        Registered<PerformanceFrameworkDeclaration> framework,
        string scoreType,
        string subjectId,
        decimal value,
        CancellationToken ct)
    {
        var score = new ScoreRecord
        {
            TenantId = kernelEvent.TenantId,
            SubjectType = "Performance",
            SubjectId = subjectId,
            ScoreType = scoreType,
            Value = value,
            PackageId = framework.PackageId,
            PackageVersion = framework.PackageVersion,
            RuleId = framework.Declaration.FrameworkId,
            Provenance = Provenance.AIGenerated,
            CorrelationId = kernelEvent.CorrelationId,
            ComputedAt = DateTimeOffset.UtcNow
        };

        await _scores.AppendAsync(score, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = kernelEvent.TenantId,
            CorrelationId = kernelEvent.CorrelationId,
            Category = AuditCategories.ScoreComputed,
            Actor = "system:flywheel",
            Summary = $"{scoreType} '{subjectId}' = {value} (framework '{framework.Declaration.FrameworkId}' of {framework.PackageId} v{framework.PackageVersion}).",
            Detail = new Dictionary<string, string>
            {
                ["scoreType"] = scoreType,
                ["subjectId"] = subjectId,
                ["value"] = value.ToString(CultureInfo.InvariantCulture),
                ["frameworkId"] = framework.Declaration.FrameworkId,
                ["packageId"] = framework.PackageId,
                ["packageVersion"] = framework.PackageVersion
            }
        }, ct);

        return score;
    }
}
