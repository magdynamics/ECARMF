using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Analytics;

/// <summary>Tenant-scoped benchmark configuration store.</summary>
public interface IBenchmarkStore
{
    Task<Benchmark?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Benchmark>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Benchmark>> GetEnabledAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(Benchmark benchmark, CancellationToken ct = default);
    Task UpdateAsync(Benchmark benchmark, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default);
}

public interface IBenchmarkMonitor
{
    /// <summary>Checks a computed score against every enabled score-kind
    /// benchmark of its tenant.</summary>
    Task CheckScoreAsync(ScoreRecord score, CancellationToken ct = default);

    /// <summary>Checks an incoming record's payload against every enabled
    /// recordField-kind benchmark of its tenant.</summary>
    Task CheckRecordAsync(
        string tenantId, string recordType, IReadOnlyDictionary<string, string> payload,
        Guid correlationId, CancellationToken ct = default);
}

/// <summary>
/// Evaluates tenant expectations wherever numbers flow: every score computed
/// and every record received. A breach raises the full alarm chain — a
/// DeviationAlert (dashboard feed), a notification to the configured role,
/// optionally a review task — and is audited. Silence is never the response
/// to a broken expectation.
/// </summary>
public class BenchmarkMonitorService : IBenchmarkMonitor
{
    private readonly IBenchmarkStore _benchmarks;
    private readonly IDeviationStore _alerts;
    private readonly INotificationStore _notifications;
    private readonly ITaskStore _tasks;
    private readonly IAuditLog _audit;

    public BenchmarkMonitorService(
        IBenchmarkStore benchmarks,
        IDeviationStore alerts,
        INotificationStore notifications,
        ITaskStore tasks,
        IAuditLog audit)
    {
        _benchmarks = benchmarks;
        _alerts = alerts;
        _notifications = notifications;
        _tasks = tasks;
        _audit = audit;
    }

    public async Task CheckScoreAsync(ScoreRecord score, CancellationToken ct = default)
    {
        var enabled = await _benchmarks.GetEnabledAsync(score.TenantId, ct);
        foreach (var benchmark in enabled.Where(b =>
                     string.Equals(b.Kind, "score", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(b.MetricType, score.ScoreType, StringComparison.OrdinalIgnoreCase)
                     && (string.IsNullOrWhiteSpace(b.SubjectId)
                         || string.Equals(b.SubjectId, score.SubjectId, StringComparison.OrdinalIgnoreCase))))
        {
            if (!Holds(benchmark, score.Value))
            {
                await RaiseAsync(benchmark, score.Value,
                    $"{score.SubjectType}:{score.SubjectId}", score.CorrelationId, score.UnitRef, ct);
            }
        }
    }

    public async Task CheckRecordAsync(
        string tenantId, string recordType, IReadOnlyDictionary<string, string> payload,
        Guid correlationId, CancellationToken ct = default)
    {
        var enabled = await _benchmarks.GetEnabledAsync(tenantId, ct);
        foreach (var benchmark in enabled.Where(b =>
                     string.Equals(b.Kind, "recordField", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(b.RecordType, recordType, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(b.Field)))
        {
            var raw = payload.FirstOrDefault(kv =>
                string.Equals(kv.Key, benchmark.Field, StringComparison.OrdinalIgnoreCase)).Value;
            if (raw is null || !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                continue; // a missing field is not a breach of a value expectation
            }

            if (!Holds(benchmark, value))
            {
                var unitRef = payload.FirstOrDefault(kv =>
                    string.Equals(kv.Key, "unitRef", StringComparison.OrdinalIgnoreCase)).Value;
                await RaiseAsync(benchmark, value, $"{recordType}", correlationId,
                    string.IsNullOrWhiteSpace(unitRef) ? null : unitRef, ct);
            }
        }
    }

    /// <summary>Does the observation satisfy the expectation?</summary>
    internal static bool Holds(Benchmark benchmark, decimal observed) => benchmark.ExpectationOperator switch
    {
        ConditionOperator.Equals => observed == benchmark.ExpectedValue,
        ConditionOperator.NotEquals => observed != benchmark.ExpectedValue,
        ConditionOperator.GreaterThan => observed > benchmark.ExpectedValue,
        ConditionOperator.LessThan => observed < benchmark.ExpectedValue,
        ConditionOperator.GreaterOrEqual => observed >= benchmark.ExpectedValue,
        ConditionOperator.LessOrEqual => observed <= benchmark.ExpectedValue,
        _ => true
    };

    private async Task RaiseAsync(
        Benchmark benchmark, decimal observed, string entityReference, Guid correlationId,
        string? unitRef, CancellationToken ct)
    {
        var expected = benchmark.ExpectedValue;
        var variance = expected != 0 ? (observed - expected) / Math.Abs(expected) : observed;
        var message =
            $"Benchmark '{benchmark.Name}' breached: observed {observed} violates the expectation " +
            $"'{benchmark.ExpectationOperator} {expected}'.";

        await _alerts.AddAsync(new DeviationAlert
        {
            TenantId = benchmark.TenantId,
            EntityReference = entityReference,
            MetricType = benchmark.Kind == "score" ? benchmark.MetricType : $"{benchmark.RecordType}.{benchmark.Field}",
            ActualValue = observed,
            ExpectedValue = expected,
            ExpectedValueSource = "Benchmark",
            VarianceMagnitude = Math.Round(variance, 6),
            ThresholdBreached = expected,
            Severity = benchmark.Severity,
            UnitRef = unitRef,
            CorrelationId = correlationId
        }, ct);

        await _notifications.AddAsync(new NotificationItem
        {
            TenantId = benchmark.TenantId,
            WorkflowId = $"benchmark:{benchmark.Id}",
            Target = benchmark.NotifyRole,
            Message = message,
            Severity = benchmark.Severity,
            CorrelationId = correlationId
        }, ct);

        if (benchmark.CreateTask)
        {
            await _tasks.AddAsync(new TaskItem
            {
                TenantId = benchmark.TenantId,
                WorkflowId = $"benchmark:{benchmark.Id}",
                Title = $"Investigate benchmark breach: {benchmark.Name} (observed {observed}, expected {benchmark.ExpectationOperator} {expected})",
                Assignee = benchmark.NotifyRole,
                Severity = benchmark.Severity,
                CorrelationId = correlationId
            }, ct);
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = benchmark.TenantId,
            CorrelationId = correlationId,
            Category = AuditCategories.BenchmarkBreached,
            Actor = "system:flywheel",
            Summary = message,
            Detail = new Dictionary<string, string>
            {
                ["benchmarkId"] = benchmark.Id.ToString(),
                ["benchmarkName"] = benchmark.Name,
                ["observed"] = observed.ToString(CultureInfo.InvariantCulture),
                ["expectation"] = $"{benchmark.ExpectationOperator} {expected}",
                ["severity"] = benchmark.Severity,
                ["notifyRole"] = benchmark.NotifyRole,
                ["entityReference"] = entityReference
            }
        }, ct);
    }
}
