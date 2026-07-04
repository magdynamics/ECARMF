using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Capital;

/// <summary>Persistence port for allocation recommendations.</summary>
public interface IAllocationStore
{
    Task AddAsync(AllocationRecommendation recommendation, CancellationToken ct = default);
    Task<AllocationRecommendation?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task UpdateDecisionAsync(AllocationRecommendation recommendation, CancellationToken ct = default);
    Task<IReadOnlyList<AllocationRecommendation>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);
}

public sealed record AllocationDecision(
    string Action,          // Approve | Modify | Reject
    decimal? ModifiedAmount,
    string? Comment);

public interface ICapitalAllocationEngine
{
    /// <summary>Generates one worked recommendation from the tenant's score
    /// history (simple explainable rule, not portfolio optimization math).</summary>
    Task<AllocationRecommendation?> GenerateAsync(string tenantId, string actorIdentifier, CancellationToken ct = default);

    Task<(bool Success, string? Error, AllocationRecommendation? Recommendation)> DecideAsync(
        string tenantId, Guid recommendationId, User decider, AllocationDecision decision, CancellationToken ct = default);
}

/// <summary>
/// MVP Capital Intelligence: recommends allocating to the subject with the
/// highest AssetReadiness score, sized by its latest Valuation, with
/// confidence from its DataConfidence — every input a ScoreRecord, every
/// recommendation explained, ranked alternatives included, and the autonomy
/// tier decided by policy. The AI (system actor) can generate but never
/// approve an Escalated recommendation.
/// </summary>
public class CapitalAllocationEngine : ICapitalAllocationEngine
{
    private readonly IScoreStore _scores;
    private readonly IAllocationStore _allocations;
    private readonly IAuditLog _audit;

    public CapitalAllocationEngine(IScoreStore scores, IAllocationStore allocations, IAuditLog audit)
    {
        _scores = scores;
        _allocations = allocations;
        _audit = audit;
    }

    public async Task<AllocationRecommendation?> GenerateAsync(
        string tenantId, string actorIdentifier, CancellationToken ct = default)
    {
        var recent = await _scores.GetRecentAsync(tenantId, 500, null, ct);

        // Candidates: subjects with an AssetReadiness score, ranked by the
        // latest reading per subject.
        var readiness = recent
            .Where(s => s.ScoreType == "AssetReadiness")
            .GroupBy(s => (s.SubjectType, s.SubjectId))
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .OrderByDescending(s => s.Value)
            .ToList();

        if (readiness.Count == 0)
        {
            return null;
        }

        var top = readiness[0];
        ScoreRecord? LatestFor(string type, string subjectId) => recent
            .Where(s => s.ScoreType == type && s.SubjectId == subjectId)
            .OrderByDescending(s => s.ComputedAt)
            .FirstOrDefault();

        var valuation = LatestFor("Valuation", top.SubjectId);
        var confidence = LatestFor("DataConfidence", top.SubjectId);
        var okr = LatestFor("OKRAttainment", top.SubjectId);

        var amount = valuation?.Value ?? 0m;
        var confidenceValue = confidence?.Value ?? 0.5m;

        var supporting = new List<Guid> { top.Id };
        if (valuation is not null) supporting.Add(valuation.Id);
        if (confidence is not null) supporting.Add(confidence.Id);
        if (okr is not null) supporting.Add(okr.Id);

        var recommendation = new AllocationRecommendation
        {
            TenantId = tenantId,
            TargetReference = top.SubjectId,
            TargetAssetClass = top.SubjectType,
            RecommendedAmount = amount,
            TargetInstitution = "treasury-primary",
            TargetJurisdiction = "US-DE",
            ConfidenceScore = confidenceValue,
            Reasoning =
                $"'{top.SubjectId}' has the highest current AssetReadiness ({top.Value}) of {readiness.Count} candidate(s); "
                + $"sizing from its latest Valuation ({amount}); confidence from DataConfidence ({confidenceValue})"
                + (okr is not null ? $"; OKRAttainment {okr.Value} supports target progress." : "."),
            Assumptions =
            [
                "Latest ScoreRecords reflect current state.",
                "Treasury has sufficient uncommitted balance.",
                "Routing via treasury-primary under the US-DE structure (defaults; no institution/jurisdiction scoring yet)."
            ],
            RiskFactors =
            [
                confidence is null ? "No DataConfidence score exists for the target — confidence defaulted to 0.5." : $"Data confidence is {confidenceValue}.",
                okr is null ? "No OKRAttainment history for the target." : $"OKRAttainment is {okr.Value}."
            ],
            AlternativesConsidered = readiness.Skip(1).Take(5).Select(s => new AllocationAlternative
            {
                TargetReference = s.SubjectId,
                Score = s.Value,
                Reason = $"AssetReadiness {s.Value} < top candidate's {top.Value}."
            }).ToList(),
            SupportingScoreRecordIds = supporting,
            CorrelationId = top.CorrelationId
        };

        recommendation.Tier = AutonomyPolicy.Default.Classify(
            amount, confidenceValue, knownTarget: true);
        recommendation.Status = recommendation.Tier == AutonomyTier.Autonomous ? "AutoExecuted" : "Pending";

        await _allocations.AddAsync(recommendation, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = recommendation.CorrelationId,
            Category = AuditCategories.AllocationRecommended,
            Actor = actorIdentifier,
            Summary = $"Allocation {recommendation.Tier}: {amount} to '{top.SubjectId}' ({recommendation.Status}). {recommendation.Reasoning}",
            Detail = new Dictionary<string, string>
            {
                ["recommendationId"] = recommendation.Id.ToString(),
                ["target"] = top.SubjectId,
                ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["confidence"] = confidenceValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["tier"] = recommendation.Tier.ToString(),
                ["alternatives"] = string.Join("; ", recommendation.AlternativesConsidered.Select(a => $"{a.TargetReference}={a.Score}"))
            }
        }, ct);

        return recommendation;
    }

    public async Task<(bool Success, string? Error, AllocationRecommendation? Recommendation)> DecideAsync(
        string tenantId, Guid recommendationId, User decider, AllocationDecision decision, CancellationToken ct = default)
    {
        var recommendation = await _allocations.GetAsync(tenantId, recommendationId, ct);
        if (recommendation is null)
        {
            return (false, "Recommendation not found for this tenant.", null);
        }

        if (recommendation.Status != "Pending")
        {
            return (false, $"Recommendation is already '{recommendation.Status}'.", null);
        }

        // The AI never self-approves an escalated outcome: escalation always
        // routes to a human role.
        if (decider.IsSystemActor)
        {
            return (false, "An AI/system actor cannot decide an allocation recommendation; escalation routes to a human role.", null);
        }

        var action = decision.Action.Trim();
        if (action is not ("Approve" or "Modify" or "Reject"))
        {
            return (false, "action must be Approve, Modify, or Reject.", null);
        }

        if (action == "Modify" && decision.ModifiedAmount is null)
        {
            return (false, "Modify requires modifiedAmount.", null);
        }

        recommendation.Status = action switch
        {
            "Approve" => "Approved",
            "Modify" => "Modified",
            _ => "Rejected"
        };
        recommendation.DecidedBy = decider.Identifier;
        recommendation.DecidedAt = DateTimeOffset.UtcNow;
        recommendation.DecisionComment = decision.Comment;
        recommendation.ModifiedAmount = action == "Modify" ? decision.ModifiedAmount : null;

        await _allocations.UpdateDecisionAsync(recommendation, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = recommendation.CorrelationId,
            Category = AuditCategories.AllocationDecided,
            Actor = decider.Identifier,
            Summary = $"Allocation recommendation {recommendation.Id} {recommendation.Status} by '{decider.Identifier}'"
                + (decision.ModifiedAmount is not null ? $" (amount modified to {decision.ModifiedAmount})" : "")
                + (string.IsNullOrWhiteSpace(decision.Comment) ? "." : $": {decision.Comment}"),
            Detail = new Dictionary<string, string>
            {
                ["recommendationId"] = recommendation.Id.ToString(),
                ["action"] = action,
                ["tier"] = recommendation.Tier.ToString(),
                ["modifiedAmount"] = decision.ModifiedAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            }
        }, ct);

        return (true, null, recommendation);
    }
}
