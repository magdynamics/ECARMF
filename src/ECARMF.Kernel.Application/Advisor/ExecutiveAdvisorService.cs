using System.Globalization;
using System.Text.Json;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Advisor;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Application.Advisor;

/// <summary>Persistence port for advisor briefs.</summary>
public interface IAdvisorStore
{
    Task AddAsync(AdvisorBrief brief, CancellationToken ct = default);
    Task<AdvisorBrief?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task UpdateFeedbackAsync(AdvisorBrief brief, CancellationToken ct = default);
    Task<IReadOnlyList<AdvisorBrief>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);
}

public interface IExecutiveAdvisor
{
    /// <summary>Reads the tenant's scores, deviations, allocations, and open
    /// tasks and produces an advisory brief under the advisor's own AI-actor
    /// identity. Advisory only: the brief never decides an outcome.</summary>
    Task<AdvisorBrief> GenerateBriefAsync(string tenantId, string requestedBy, CancellationToken ct = default);

    /// <summary>Records a human verdict on a brief and feeds it into the
    /// ModelAccuracy trust loop for the backend that produced it.</summary>
    Task<(bool Success, string? Error, AdvisorBrief? Brief)> RecordFeedbackAsync(
        string tenantId, Guid briefId, bool useful, User reviewer, CancellationToken ct = default);
}

/// <summary>
/// The first agent of the AI Decision Engine layer. The advisor aggregates
/// the tenant's operational state into a snapshot, then composes a brief —
/// via the configured language model when one is available, otherwise via a
/// deterministic composer over the same snapshot. Either way the output is
/// provenance:AIGenerated, fully audited, and trust-tracked: the LLM is a
/// better writer, not a different authority.
/// </summary>
public class ExecutiveAdvisorService : IExecutiveAdvisor
{
    public const string ActorIdentifier = "system:advisor";
    public const string DeterministicModelReference = "advisor:deterministic-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IScoreStore _scores;
    private readonly IDeviationStore _deviations;
    private readonly IAllocationStore _allocations;
    private readonly ITaskStore _tasks;
    private readonly IAdvisorStore _briefs;
    private readonly ILanguageModelClient _llm;
    private readonly IAILearningFeedbackService _feedback;
    private readonly IAuditLog _audit;

    public ExecutiveAdvisorService(
        IScoreStore scores,
        IDeviationStore deviations,
        IAllocationStore allocations,
        ITaskStore tasks,
        IAdvisorStore briefs,
        ILanguageModelClient llm,
        IAILearningFeedbackService feedback,
        IAuditLog audit)
    {
        _scores = scores;
        _deviations = deviations;
        _allocations = allocations;
        _tasks = tasks;
        _briefs = briefs;
        _llm = llm;
        _feedback = feedback;
        _audit = audit;
    }

    public async Task<AdvisorBrief> GenerateBriefAsync(
        string tenantId, string requestedBy, CancellationToken ct = default)
    {
        var snapshot = await BuildSnapshotAsync(tenantId, ct);

        AdvisorBrief brief;
        string backend;

        if (_llm.IsConfigured)
        {
            (brief, backend) = await ComposeWithModelAsync(snapshot, ct);
        }
        else
        {
            brief = ComposeDeterministic(snapshot);
            backend = "deterministic";
        }

        brief.TenantId = tenantId;
        brief.RequestedBy = requestedBy;
        brief.Provenance = Provenance.AIGenerated;

        await _briefs.AddAsync(brief, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = brief.CorrelationId,
            Category = AuditCategories.AdvisorBriefGenerated,
            Actor = ActorIdentifier,
            Summary = $"Executive Advisor produced brief '{brief.Title}' ({brief.Recommendations.Count} recommendations, backend {backend}).",
            Detail = new Dictionary<string, string>
            {
                ["briefId"] = brief.Id.ToString(),
                ["modelReference"] = brief.ModelReference,
                ["backend"] = backend,
                ["provenance"] = Provenance.AIGenerated,
                ["requestedBy"] = requestedBy,
                ["scoresConsidered"] = snapshot.ScoreAverages.Count.ToString(CultureInfo.InvariantCulture),
                ["openDeviations"] = snapshot.OpenDeviations.Count.ToString(CultureInfo.InvariantCulture),
                ["pendingAllocations"] = snapshot.PendingAllocations.Count.ToString(CultureInfo.InvariantCulture),
                ["openTasks"] = snapshot.OpenTaskCount.ToString(CultureInfo.InvariantCulture)
            }
        }, ct);

        return brief;
    }

    public async Task<(bool Success, string? Error, AdvisorBrief? Brief)> RecordFeedbackAsync(
        string tenantId, Guid briefId, bool useful, User reviewer, CancellationToken ct = default)
    {
        if (reviewer.IsSystemActor)
        {
            return (false, "An AI/system actor cannot rate advisor briefs; trust is earned from humans.", null);
        }

        var brief = await _briefs.GetAsync(tenantId, briefId, ct);
        if (brief is null)
        {
            return (false, "Brief not found.", null);
        }

        if (brief.FeedbackUseful is not null)
        {
            return (false, "Feedback has already been recorded for this brief.", null);
        }

        brief.FeedbackUseful = useful;
        brief.FeedbackBy = reviewer.Identifier;
        brief.FeedbackAt = DateTimeOffset.UtcNow;
        await _briefs.UpdateFeedbackAsync(brief, ct);

        // The advisor implicitly predicts its advice is useful; the human
        // verdict is the actual. Same accuracy loop as every other AI output.
        await _feedback.PublishModelAccuracyAsync(
            tenantId, brief.CorrelationId, "Useful", useful ? "Useful" : "NotUseful",
            brief.ModelReference, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = brief.CorrelationId,
            Category = AuditCategories.AdvisorFeedbackRecorded,
            Actor = reviewer.Identifier,
            Summary = $"'{reviewer.Identifier}' rated brief '{brief.Title}' as {(useful ? "useful" : "not useful")}.",
            Detail = new Dictionary<string, string>
            {
                ["briefId"] = brief.Id.ToString(),
                ["useful"] = useful.ToString(),
                ["modelReference"] = brief.ModelReference
            }
        }, ct);

        return (true, null, brief);
    }

    // ---- snapshot -------------------------------------------------------

    internal sealed record ScoreAverage(string ScoreType, decimal Average, int Count);

    internal sealed record DeviationSummary(string EntityReference, string MetricType, string Severity, decimal VarianceMagnitude);

    internal sealed record AllocationSummary(string TargetReference, decimal RecommendedAmount, string Tier, decimal ConfidenceScore);

    internal sealed record AdvisorSnapshot(
        string TenantId,
        IReadOnlyList<ScoreAverage> ScoreAverages,
        IReadOnlyList<DeviationSummary> OpenDeviations,
        IReadOnlyList<AllocationSummary> PendingAllocations,
        int OpenTaskCount,
        decimal? ModelAccuracyAverage,
        int ModelAccuracySamples);

    private async Task<AdvisorSnapshot> BuildSnapshotAsync(string tenantId, CancellationToken ct)
    {
        var recentScores = await _scores.GetRecentAsync(tenantId, 500, null, ct);
        var deviations = await _deviations.GetRecentAsync(tenantId, 100, ct);
        var allocations = await _allocations.GetRecentAsync(tenantId, 50, ct);
        var tasks = await _tasks.GetRecentAsync(tenantId, 200, ct);

        var averages = recentScores
            .Where(s => s.ScoreType != "ModelAccuracy")
            .GroupBy(s => s.ScoreType)
            .Select(g => new ScoreAverage(g.Key, Math.Round(g.Average(s => s.Value), 4), g.Count()))
            .OrderBy(a => a.ScoreType)
            .ToList();

        var openDeviations = deviations
            .Where(d => d.ResolvedAt is null && d.AcknowledgedBy is null)
            .OrderByDescending(d => d.Severity == "Critical")
            .ThenByDescending(d => Math.Abs(d.VarianceMagnitude))
            .Take(20)
            .Select(d => new DeviationSummary(d.EntityReference, d.MetricType, d.Severity, Math.Round(d.VarianceMagnitude, 4)))
            .ToList();

        var pendingAllocations = allocations
            .Where(a => a.Status == "Pending")
            .OrderByDescending(a => a.Tier == Domain.Capital.AutonomyTier.Escalated)
            .ThenByDescending(a => a.RecommendedAmount)
            .Take(10)
            .Select(a => new AllocationSummary(a.TargetReference, a.RecommendedAmount, a.Tier.ToString(), a.ConfidenceScore))
            .ToList();

        var accuracy = recentScores.Where(s => s.ScoreType == "ModelAccuracy").ToList();

        return new AdvisorSnapshot(
            tenantId,
            averages,
            openDeviations,
            pendingAllocations,
            tasks.Count(t => t.Status == "Open"),
            accuracy.Count > 0 ? Math.Round(accuracy.Average(s => s.Value), 4) : null,
            accuracy.Count);
    }

    // ---- deterministic composer -----------------------------------------

    private AdvisorBrief ComposeDeterministic(AdvisorSnapshot snapshot)
    {
        var recommendations = new List<AdvisorRecommendation>();
        var summaryParts = new List<string>();

        var critical = snapshot.OpenDeviations.Where(d => d.Severity == "Critical").ToList();
        var warnings = snapshot.OpenDeviations.Where(d => d.Severity != "Critical").ToList();

        if (critical.Count > 0)
        {
            summaryParts.Add($"{critical.Count} critical deviation(s) are unacknowledged.");
            foreach (var deviation in critical.Take(3))
            {
                recommendations.Add(new AdvisorRecommendation
                {
                    Recommendation = $"Investigate the critical {deviation.MetricType} deviation on '{deviation.EntityReference}'.",
                    Rationale = $"Actual diverges from expectation by {deviation.VarianceMagnitude:P0} — beyond twice the monitoring threshold and still unacknowledged.",
                    Priority = "High"
                });
            }
        }

        if (warnings.Count > 0)
        {
            summaryParts.Add($"{warnings.Count} warning-level deviation(s) await review.");
            recommendations.Add(new AdvisorRecommendation
            {
                Recommendation = "Review and acknowledge the open warning-level deviations.",
                Rationale = "Unacknowledged deviations accumulate silently; the monitoring loop treats silence as a failure mode.",
                Priority = "Medium"
            });
        }

        var escalated = snapshot.PendingAllocations.Where(a => a.Tier == "Escalated").ToList();
        if (escalated.Count > 0)
        {
            summaryParts.Add($"{escalated.Count} escalated allocation recommendation(s) require a human decision.");
            foreach (var allocation in escalated.Take(3))
            {
                recommendations.Add(new AdvisorRecommendation
                {
                    Recommendation = $"Decide the escalated allocation of {allocation.RecommendedAmount:N0} to '{allocation.TargetReference}'.",
                    Rationale = $"The AI flagged and stopped (confidence {allocation.ConfidenceScore:P0}); by policy it can never self-approve an escalation.",
                    Priority = "High"
                });
            }
        }

        var otherPending = snapshot.PendingAllocations.Count - escalated.Count;
        if (otherPending > 0)
        {
            summaryParts.Add($"{otherPending} allocation recommendation(s) are pending review.");
            recommendations.Add(new AdvisorRecommendation
            {
                Recommendation = "Approve, modify, or reject the pending allocation recommendations.",
                Rationale = "Recommend-only tier allocations do not execute until a human decides.",
                Priority = "Medium"
            });
        }

        if (snapshot.OpenTaskCount > 0)
        {
            summaryParts.Add($"{snapshot.OpenTaskCount} workflow task(s) are open.");
            recommendations.Add(new AdvisorRecommendation
            {
                Recommendation = $"Work the {snapshot.OpenTaskCount} open workflow task(s).",
                Rationale = "Tasks are created by declared workflows on flagged activity; they represent controls awaiting human action.",
                Priority = "Medium"
            });
        }

        if (snapshot.ModelAccuracyAverage is { } accuracy && snapshot.ModelAccuracySamples >= 3 && accuracy < 0.7m)
        {
            summaryParts.Add($"AI model accuracy is {accuracy:P0} over {snapshot.ModelAccuracySamples} verdicts.");
            recommendations.Add(new AdvisorRecommendation
            {
                Recommendation = "Review recent AI-flagged outcomes against human verdicts and consider a rule-threshold package revision.",
                Rationale = $"ModelAccuracy of {accuracy:P0} means human reviewers frequently overturn AI predictions; thresholds ship as new package versions, never silent edits.",
                Priority = "High"
            });
        }

        if (snapshot.ScoreAverages.Count > 0)
        {
            var top = string.Join(", ", snapshot.ScoreAverages.Take(5).Select(a => $"{a.ScoreType} avg {a.Average}"));
            summaryParts.Add($"Score activity: {top}.");
        }

        if (summaryParts.Count == 0)
        {
            summaryParts.Add("No score, deviation, allocation, or task activity was found for this tenant.");
            recommendations.Add(new AdvisorRecommendation
            {
                Recommendation = "Activate a knowledge package and submit records to start the flywheel.",
                Rationale = "The advisor synthesizes what the kernel has processed; with no activity there is nothing to advise on yet.",
                Priority = "Low"
            });
        }

        return new AdvisorBrief
        {
            Title = $"Executive Brief — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            ExecutiveSummary = string.Join(" ", summaryParts),
            Recommendations = recommendations,
            ModelReference = DeterministicModelReference
        };
    }

    // ---- LLM composer ----------------------------------------------------

    internal sealed class BriefPayload
    {
        public string Title { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<AdvisorRecommendation> Recommendations { get; set; } = [];
    }

    private async Task<(AdvisorBrief Brief, string Backend)> ComposeWithModelAsync(
        AdvisorSnapshot snapshot, CancellationToken ct)
    {
        const string systemPrompt =
            "You are the Executive Advisor agent of the ECARMF platform kernel — an enterprise " +
            "governance, risk, and capital-intelligence system. You receive a JSON snapshot of one " +
            "tenant's operational state: score averages by type, open deviation alerts, pending " +
            "capital allocation recommendations, open workflow tasks, and the platform's measured " +
            "accuracy of prior AI predictions. Produce a concise executive brief for the tenant's " +
            "owner. You advise only; you never decide or execute. Ground every recommendation in the " +
            "snapshot data and give each a rationale. Respond ONLY with a JSON object of this exact " +
            "shape and nothing else: {\"title\": string, \"executiveSummary\": string, " +
            "\"recommendations\": [{\"recommendation\": string, \"rationale\": string, " +
            "\"priority\": \"High\"|\"Medium\"|\"Low\"}]}";

        var userPrompt = JsonSerializer.Serialize(snapshot, JsonOptions);

        try
        {
            var raw = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
            var payload = ParseBriefJson(raw);
            if (payload is not null
                && !string.IsNullOrWhiteSpace(payload.ExecutiveSummary)
                && payload.Recommendations.Count > 0)
            {
                return (new AdvisorBrief
                {
                    Title = string.IsNullOrWhiteSpace(payload.Title)
                        ? $"Executive Brief — {DateTimeOffset.UtcNow:yyyy-MM-dd}"
                        : payload.Title,
                    ExecutiveSummary = payload.ExecutiveSummary,
                    Recommendations = payload.Recommendations,
                    ModelReference = $"advisor:{_llm.ModelReference}"
                }, "llm");
            }
        }
        catch
        {
            // Fall through: a degraded model backend must never take the
            // advisor down. The deterministic composer covers the same data.
        }

        return (ComposeDeterministic(snapshot), "deterministic-fallback");
    }

    /// <summary>Extracts the JSON object from a model response, tolerating
    /// markdown code fences or prose around it.</summary>
    internal static BriefPayload? ParseBriefJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BriefPayload>(raw[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
