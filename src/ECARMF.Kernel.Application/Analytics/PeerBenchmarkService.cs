using ECARMF.Kernel.Application.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

/// <summary>The comparison no one else can give the client: their number
/// against the anonymized distribution of comparable businesses on the
/// platform. Peer identities never leave the aggregation.</summary>
public record PeerBenchmarkResult(
    string ScoreType,
    bool Available,
    string? Reason,
    decimal? YourAverage,
    decimal? YourLatest,
    int PeerCount,
    decimal? PeerMedian,
    decimal? PeerP25,
    decimal? PeerP75);

public interface IPeerBenchmarkService
{
    Task<PeerBenchmarkResult> CompareAsync(
        string tenantId, string scoreType, CancellationToken ct = default);
}

/// <summary>
/// Anonymized peer benchmarking. Each peer contributes ONE number (its own
/// average of the score type) to the distribution; the requesting tenant is
/// excluded from the peer set; and nothing is disclosed below the minimum
/// peer count — with two peers, a median would identify the other business.
/// </summary>
public class PeerBenchmarkService : IPeerBenchmarkService
{
    public const int MinimumPeers = 3;

    private readonly IScoreStore _scores;

    public PeerBenchmarkService(IScoreStore scores) => _scores = scores;

    public async Task<PeerBenchmarkResult> CompareAsync(
        string tenantId, string scoreType, CancellationToken ct = default)
    {
        var all = await _scores.GetRecentByTypeAllTenantsAsync(scoreType, 5000, ct);

        var mine = all.Where(s => string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList();
        decimal? myAverage = mine.Count > 0 ? Math.Round(mine.Average(s => s.Value), 4) : null;
        decimal? myLatest = mine.Count > 0
            ? Math.Round(mine.OrderByDescending(s => s.ComputedAt).First().Value, 4)
            : null;

        var peerAverages = all
            .Where(s => !string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.TenantId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Average(s => s.Value))
            .OrderBy(v => v)
            .ToList();

        if (peerAverages.Count < MinimumPeers)
        {
            return new PeerBenchmarkResult(scoreType, false,
                $"Fewer than {MinimumPeers} comparable businesses have this metric — " +
                "the comparison is withheld to protect peer anonymity.",
                myAverage, myLatest, peerAverages.Count, null, null, null);
        }

        return new PeerBenchmarkResult(scoreType, true, null, myAverage, myLatest,
            peerAverages.Count,
            Percentile(peerAverages, 0.50m),
            Percentile(peerAverages, 0.25m),
            Percentile(peerAverages, 0.75m));
    }

    /// <summary>Linear-interpolated percentile over a sorted list.</summary>
    internal static decimal Percentile(IReadOnlyList<decimal> sorted, decimal p)
    {
        if (sorted.Count == 1)
        {
            return Math.Round(sorted[0], 4);
        }

        var rank = p * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        var fraction = rank - low;
        return Math.Round(sorted[low] + (sorted[high] - sorted[low]) * fraction, 4);
    }
}
