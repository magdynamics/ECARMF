using System.Globalization;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Processing;

/// <summary>One rule's evaluation during event processing, kept for the audit trail.</summary>
public sealed record RuleEvaluation(
    string RuleId,
    string PackageId,
    string PackageVersion,
    bool Matched);

/// <summary>What processing an event produced.</summary>
public sealed record ProcessingResult(
    string EventName,
    Guid CorrelationId,
    IReadOnlyList<RuleEvaluation> Evaluations,
    TransactionOutcome? Outcome);

public interface IEventProcessor
{
    Task<ProcessingResult> ProcessAsync(KernelEvent kernelEvent, CancellationToken ct = default);
}

/// <summary>
/// The kernel's rule execution mechanism. Consumes an event, evaluates the
/// tenant's subscribed rules in registry order, and produces an explainable
/// outcome: first matching rule fires and decides. When no rule matches a
/// TransactionReceived event, the default policy approves — controls are
/// exception-based, and the default is itself recorded and explained.
/// </summary>
public class EventProcessor : IEventProcessor
{
    private readonly ITenantRegistryProvider _registries;
    private readonly IOutcomeStore _outcomes;
    private readonly IScoreStore _scores;
    private readonly IKernelEventBus _bus;
    private readonly IAuditLog _audit;

    public EventProcessor(
        ITenantRegistryProvider registries,
        IOutcomeStore outcomes,
        IScoreStore scores,
        IKernelEventBus bus,
        IAuditLog audit)
    {
        _registries = registries;
        _outcomes = outcomes;
        _scores = scores;
        _bus = bus;
        _audit = audit;
    }

    public async Task<ProcessingResult> ProcessAsync(KernelEvent kernelEvent, CancellationToken ct = default)
    {
        var registries = _registries.GetFor(kernelEvent.TenantId);
        var subscribed = registries.Rules.GetRulesForEvent(kernelEvent.EventName);
        var evaluations = new List<RuleEvaluation>();
        Registered<RuleDeclaration>? fired = null;

        var emittedScores = new List<ScoreRecord>();

        foreach (var rule in subscribed)
        {
            var matched = rule.Declaration.Conditions.Count > 0
                && rule.Declaration.Conditions.All(c => ConditionEvaluator.Matches(c, kernelEvent.Payload));

            evaluations.Add(new RuleEvaluation(
                rule.Declaration.RuleId, rule.PackageId, rule.PackageVersion, matched));

            if (!matched)
            {
                continue;
            }

            emittedScores.AddRange(BuildScores(rule, kernelEvent));

            // Scoring-only rules (no outcome) fire and processing continues;
            // the first matching rule WITH an outcome decides and stops.
            if (!string.IsNullOrWhiteSpace(rule.Declaration.OutcomeOnMatch))
            {
                fired = rule;
                break;
            }
        }

        foreach (var score in emittedScores)
        {
            await _scores.AppendAsync(score, ct);
            await _audit.AppendAsync(new AuditEntry
            {
                TenantId = kernelEvent.TenantId,
                CorrelationId = kernelEvent.CorrelationId,
                Category = AuditCategories.ScoreComputed,
                Actor = "system:flywheel",
                Summary = $"Score '{score.ScoreType}' = {score.Value} computed for {score.SubjectType} '{score.SubjectId}' by rule '{score.RuleId}'.",
                Detail = new Dictionary<string, string>
                {
                    ["scoreType"] = score.ScoreType,
                    ["value"] = score.Value.ToString(CultureInfo.InvariantCulture),
                    ["subjectType"] = score.SubjectType,
                    ["subjectId"] = score.SubjectId,
                    ["ruleId"] = score.RuleId ?? string.Empty,
                    ["packageId"] = score.PackageId ?? string.Empty,
                    ["packageVersion"] = score.PackageVersion ?? string.Empty
                }
            }, ct);
        }

        // Every rule evaluation is audited, matched or not, before anything
        // downstream happens (audit integrity first).
        var evaluationEntries = evaluations.Select(e => new AuditEntry
        {
            TenantId = kernelEvent.TenantId,
            CorrelationId = kernelEvent.CorrelationId,
            Category = AuditCategories.RuleEvaluated,
            Actor = "system:flywheel",
            Summary = $"Rule '{e.RuleId}' evaluated for event '{kernelEvent.EventName}': {(e.Matched ? "matched" : "did not match")}.",
            Detail = new Dictionary<string, string>
            {
                ["ruleId"] = e.RuleId,
                ["packageId"] = e.PackageId,
                ["packageVersion"] = e.PackageVersion,
                ["eventName"] = kernelEvent.EventName,
                ["matched"] = e.Matched.ToString()
            }
        }).ToList();

        await _audit.AppendManyAsync(evaluationEntries, ct);

        var isIntakeEvent = string.Equals(
            kernelEvent.EventName, KernelEventNames.RecordReceived, StringComparison.OrdinalIgnoreCase);

        // An outcome is recorded whenever a rule fires, and always for the
        // intake event (where the default-approve policy applies). Follow-up
        // events with no matching rules produce no outcome noise.
        if (fired is null && !isIntakeEvent)
        {
            return new ProcessingResult(kernelEvent.EventName, kernelEvent.CorrelationId, evaluations, null);
        }

        var outcome = new TransactionOutcome
        {
            TenantId = kernelEvent.TenantId,
            TransactionId = kernelEvent.CorrelationId,
            EventName = kernelEvent.EventName,
            Outcome = fired?.Declaration.OutcomeOnMatch ?? KernelOutcomes.Approved,
            Reason = fired is not null
                ? ReasonRenderer.Render(fired.Declaration.ReasonTemplate, kernelEvent.Payload)
                : "No active rule objected; approved by default kernel policy.",
            RuleId = fired?.Declaration.RuleId,
            PackageId = fired?.PackageId,
            PackageVersion = fired?.PackageVersion,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        await _outcomes.AppendAsync(outcome, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = kernelEvent.TenantId,
            CorrelationId = kernelEvent.CorrelationId,
            Category = AuditCategories.OutcomeRecorded,
            Actor = "system:flywheel",
            Summary = $"Outcome '{outcome.Outcome}' recorded for event '{kernelEvent.EventName}': {outcome.Reason}",
            Detail = new Dictionary<string, string>
            {
                ["outcome"] = outcome.Outcome,
                ["reason"] = outcome.Reason,
                ["ruleId"] = outcome.RuleId ?? string.Empty,
                ["packageId"] = outcome.PackageId ?? string.Empty,
                ["packageVersion"] = outcome.PackageVersion ?? string.Empty,
                ["eventName"] = kernelEvent.EventName
            }
        }, ct);

        // The follow-up event name IS the outcome string (Approved, Flagged,
        // Hold, Escalate, …) — packages define both. Only intake processing
        // emits outcome events, so rules reacting to them can never cycle.
        if (isIntakeEvent)
        {
            var followUpEvent = outcome.Outcome;
            if (registries.Events.IsDeclared(followUpEvent))
            {
                var payload = new Dictionary<string, string>(kernelEvent.Payload, StringComparer.OrdinalIgnoreCase)
                {
                    ["outcome"] = outcome.Outcome,
                    ["reason"] = outcome.Reason
                };

                await _bus.PublishAsync(new KernelEvent(
                    kernelEvent.TenantId, followUpEvent, kernelEvent.CorrelationId, payload, DateTimeOffset.UtcNow), ct);
            }
        }

        return new ProcessingResult(kernelEvent.EventName, kernelEvent.CorrelationId, evaluations, outcome);
    }

    /// <summary>Resolves a matched rule's declared score emissions against the
    /// event payload. Values are literals or {field} tokens; an unresolvable
    /// value skips that emission (visible via the missing ScoreComputed entry).</summary>
    private static IEnumerable<ScoreRecord> BuildScores(
        Registered<RuleDeclaration> rule, KernelEvent kernelEvent)
    {
        foreach (var emission in rule.Declaration.EmitScores)
        {
            var rendered = ReasonRenderer.Render(emission.Value, kernelEvent.Payload);
            if (!decimal.TryParse(rendered, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var subjectId = kernelEvent.CorrelationId.ToString();
            if (!string.IsNullOrWhiteSpace(emission.SubjectIdField))
            {
                var fromPayload = kernelEvent.Payload.FirstOrDefault(kv =>
                    string.Equals(kv.Key, emission.SubjectIdField, StringComparison.OrdinalIgnoreCase)).Value;
                if (!string.IsNullOrWhiteSpace(fromPayload))
                {
                    subjectId = fromPayload;
                }
            }

            var subjectType = emission.SubjectType;
            if (string.IsNullOrWhiteSpace(subjectType))
            {
                subjectType = kernelEvent.Payload.FirstOrDefault(kv =>
                    string.Equals(kv.Key, "recordType", StringComparison.OrdinalIgnoreCase)).Value ?? "Record";
            }

            yield return new ScoreRecord
            {
                TenantId = kernelEvent.TenantId,
                SubjectType = subjectType,
                SubjectId = subjectId,
                ScoreType = emission.ScoreType,
                Value = value,
                RuleId = rule.Declaration.RuleId,
                PackageId = rule.PackageId,
                PackageVersion = rule.PackageVersion,
                CorrelationId = kernelEvent.CorrelationId,
                ComputedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
