using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Relationships;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Relationships;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

public interface ICompositeHealthService
{
    /// <summary>Computes a rollup score for a parent subject and appends it as
    /// a ScoreRecord (scoreType CompositeHealth). Returns null when the parent
    /// has no RollsUpInto edges or none of the children have a score yet.</summary>
    Task<ScoreRecord?> ComputeAsync(
        string tenantId, string subjectType, string subjectId,
        string? childScoreType = null, string compositeScoreType = CompositeHealthService.DefaultScoreType,
        CancellationToken ct = default);
}

/// <summary>
/// Composite / rollup health score (Batch 3, Refinement 14). NOT a new
/// storage primitive and NOT kernel domain logic — it is the documented
/// PATTERN for an executive rollup, assembled from primitives that already
/// exist: RollsUpInto EntityRelationship edges (Refinement 13) name the child
/// subjects and carry each one's weight in Strength; the child ScoreRecords'
/// latest values are aggregated by CalculateWeightedRiskScore (Refinement 16);
/// the result is written back as an ordinary ScoreRecord with
/// scoreType "CompositeHealth" and provenance AIGenerated. A tenant wanting an
/// executive rollup wires up the edges and calls this — it does not reinvent
/// the mechanism. WHICH children and WHAT weights are tenant/package config
/// (the edges); the arithmetic and the ScoreRecord discipline are the kernel's.
/// </summary>
public class CompositeHealthService : ICompositeHealthService
{
    public const string DefaultScoreType = "CompositeHealth";

    private readonly IEntityRelationshipStore _relationships;
    private readonly IScoreStore _scores;
    private readonly IAuditLog _audit;

    public CompositeHealthService(
        IEntityRelationshipStore relationships, IScoreStore scores, IAuditLog audit)
    {
        _relationships = relationships;
        _scores = scores;
        _audit = audit;
    }

    public async Task<ScoreRecord?> ComputeAsync(
        string tenantId, string subjectType, string subjectId,
        string? childScoreType = null, string compositeScoreType = DefaultScoreType,
        CancellationToken ct = default)
    {
        var edges = await _relationships.GetBySubjectAsync(
            tenantId, subjectType, subjectId, RelationshipTypes.RollsUpInto, ct);
        if (edges.Count == 0)
        {
            return null;
        }

        var factors = new List<WeightedFactor>();
        var contributions = new Dictionary<string, string>();
        foreach (var edge in edges)
        {
            var latest = await LatestChildScoreAsync(tenantId, edge, childScoreType, ct);
            if (latest is null)
            {
                continue;
            }

            // A null edge weight means "count this child equally" (weight 1).
            var weight = edge.Strength ?? 1m;
            factors.Add(new WeightedFactor($"{edge.RelatedType}:{edge.RelatedId}", latest.Value, weight));
            contributions[$"{edge.RelatedType}:{edge.RelatedId}"] =
                $"value={latest.Value} weight={weight} scoreType={latest.ScoreType}";
        }

        if (factors.Count == 0)
        {
            return null;
        }

        var rollup = StatisticalFunctionLibrary.CalculateWeightedRiskScore(factors);
        var correlationId = Guid.NewGuid();
        var score = new ScoreRecord
        {
            TenantId = tenantId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ScoreType = compositeScoreType,
            Value = Math.Round(rollup, 6),
            RuleId = "composite-health-rollup-v1",
            Provenance = Provenance.AIGenerated,
            CorrelationId = correlationId,
            Metadata = new Dictionary<string, string>
            {
                ["childCount"] = factors.Count.ToString(),
                ["aggregation"] = "weighted-mean"
            }
        };
        foreach (var (key, detail) in contributions)
        {
            score.Metadata[$"child:{key}"] = detail;
        }

        await _scores.AppendAsync(score, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = AuditCategories.CompositeHealthComputed,
            Actor = "system:flywheel",
            Summary = $"Composite '{compositeScoreType}' = {score.Value} for {subjectType} '{subjectId}' " +
                      $"rolled up from {factors.Count} child score(s).",
            Detail = score.Metadata
        }, ct);

        return score;
    }

    private async Task<ScoreRecord?> LatestChildScoreAsync(
        string tenantId, EntityRelationship edge, string? childScoreType, CancellationToken ct)
    {
        var history = await _scores.GetHistoryAsync(tenantId, edge.RelatedType, edge.RelatedId, ct);
        var candidates = history.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(childScoreType))
        {
            candidates = candidates.Where(s =>
                string.Equals(s.ScoreType, childScoreType, StringComparison.OrdinalIgnoreCase));
        }
        // History is oldest-first; the last match is the current value.
        return candidates.LastOrDefault();
    }
}
