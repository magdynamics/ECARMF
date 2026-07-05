using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Scoring;

/// <summary>Append-only persistence port for ScoreRecords. Reads are
/// tenant-scoped; history order is oldest first.</summary>
public interface IScoreStore
{
    Task AppendAsync(ScoreRecord score, CancellationToken ct = default);

    Task<IReadOnlyList<ScoreRecord>> GetHistoryAsync(
        string tenantId, string subjectType, string subjectId, CancellationToken ct = default);

    /// <summary>Most recent scores for a tenant, newest first, optionally
    /// filtered by score type. Feeds dashboards and KPI queries.</summary>
    Task<IReadOnlyList<ScoreRecord>> GetRecentAsync(
        string tenantId, int limit, string? scoreType = null, CancellationToken ct = default);

    /// <summary>Recent scores of one type across ALL tenants, newest first.
    /// Used only by anonymized peer benchmarking — callers must aggregate
    /// before anything leaves the platform boundary.</summary>
    Task<IReadOnlyList<ScoreRecord>> GetRecentByTypeAllTenantsAsync(
        string scoreType, int limit, CancellationToken ct = default);
}
