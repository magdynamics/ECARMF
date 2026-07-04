using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Flywheel;

public interface IAILearningFeedbackService
{
    /// <summary>Publishes a predicted-vs-actual comparison back into the
    /// pipeline as a ModelAccuracy ScoreRecord tagged provenance:AIGenerated.</summary>
    Task PublishModelAccuracyAsync(
        string tenantId,
        Guid cycleCorrelationId,
        string predictedOutcome,
        string actualOutcome,
        string modelReference,
        CancellationToken ct = default);
}

/// <summary>
/// The internal half of the AI Analytical source category: flywheel results
/// re-enter the pipeline as input for the next cycle. ModelAccuracy is its
/// own tracked score category (never folded into MarketRisk or anything
/// else) — it is the mechanism that stops the flywheel from reinforcing its
/// own errors in a closed loop: a model's output is only weighted more
/// heavily over time if its predictions actually match real outcomes.
/// </summary>
public class AILearningFeedbackService : IAILearningFeedbackService
{
    private readonly IScoreStore _scores;
    private readonly IAuditLog _audit;

    public AILearningFeedbackService(IScoreStore scores, IAuditLog audit)
    {
        _scores = scores;
        _audit = audit;
    }

    public async Task PublishModelAccuracyAsync(
        string tenantId,
        Guid cycleCorrelationId,
        string predictedOutcome,
        string actualOutcome,
        string modelReference,
        CancellationToken ct = default)
    {
        var accurate = string.Equals(predictedOutcome, actualOutcome, StringComparison.OrdinalIgnoreCase);

        var score = new ScoreRecord
        {
            TenantId = tenantId,
            SubjectType = "Model",
            SubjectId = modelReference,
            ScoreType = "ModelAccuracy",
            Value = accurate ? 1m : 0m,
            Provenance = Provenance.AIGenerated,
            CorrelationId = cycleCorrelationId,
            ComputedAt = DateTimeOffset.UtcNow
        };

        await _scores.AppendAsync(score, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = cycleCorrelationId,
            Category = AuditCategories.ScoreComputed,
            Actor = "system:flywheel",
            Summary = $"ModelAccuracy = {score.Value} for '{modelReference}': predicted '{predictedOutcome}', actual '{actualOutcome}'.",
            Detail = new Dictionary<string, string>
            {
                ["scoreType"] = "ModelAccuracy",
                ["value"] = score.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["predictedOutcome"] = predictedOutcome,
                ["actualOutcome"] = actualOutcome,
                ["modelReference"] = modelReference,
                ["provenance"] = Provenance.AIGenerated
            }
        }, ct);
    }
}
