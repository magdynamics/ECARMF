using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Scoring;

namespace ECARMF.Kernel.Application.Analytics;

/// <summary>One risk plotted on the platform-wide heatmap.</summary>
public sealed record PlatformRiskPoint(
    string TenantId, string Domain, int Severity, int Likelihood, decimal Index, string Label);

/// <summary>A tenant's risk posture at a glance.</summary>
public sealed record TenantRiskSummary(
    string TenantId, string Name, int Risks, int Critical, decimal WorstIndex, IReadOnlyList<string> TopDomains);

public sealed record PlatformRiskOverview(
    int TotalRisks,
    int CriticalRisks,
    int TenantsWithRisk,
    int TenantsTotal,
    IReadOnlyList<TenantRiskSummary> Tenants,
    IReadOnlyList<PlatformRiskPoint> Heatmap);

public interface IPlatformRiskService
{
    Task<PlatformRiskOverview> OverviewAsync(CancellationToken ct = default);
}

/// <summary>
/// The platform-wide risk picture: every tenant has risks to manage, so this
/// rolls up each tenant's risk-tagged scores into one heatmap and a per-tenant
/// posture table. A risk is one subject's latest score; critical = a top-right
/// (high severity AND high likelihood) cell.
/// </summary>
public class PlatformRiskService : IPlatformRiskService
{
    private readonly IScoreStore _scores;
    private readonly ITenantDirectory _tenants;

    public PlatformRiskService(IScoreStore scores, ITenantDirectory tenants)
    {
        _scores = scores;
        _tenants = tenants;
    }

    public async Task<PlatformRiskOverview> OverviewAsync(CancellationToken ct = default)
    {
        var allTenants = await _tenants.GetAllAsync(ct);
        var names = allTenants.ToDictionary(t => t.TenantId, t => t.Name, StringComparer.OrdinalIgnoreCase);

        var scores = await _scores.GetRecentRiskAllTenantsAsync(20000, ct);

        // One risk = the latest score per (tenant, subject). Scores arrive
        // newest-first, so the first seen per group is the current one.
        var latest = scores
            .GroupBy(s => (s.TenantId, s.SubjectId), TupleComparer)
            .Select(g => g.First())
            .ToList();

        var points = new List<PlatformRiskPoint>();
        foreach (var s in latest)
        {
            var (severity, likelihood) = SeverityLikelihood(s);
            var domain = string.IsNullOrWhiteSpace(s.RiskType) ? "General" : s.RiskType!;
            points.Add(new PlatformRiskPoint(
                s.TenantId, domain, severity, likelihood, severity * likelihood,
                $"{names.GetValueOrDefault(s.TenantId, s.TenantId)} · {domain}"));
        }

        var perTenant = points
            .GroupBy(p => p.TenantId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TenantRiskSummary(
                g.Key,
                names.GetValueOrDefault(g.Key, g.Key),
                g.Count(),
                g.Count(p => p.Severity >= 4 && p.Likelihood >= 4),
                g.Max(p => p.Index),
                g.GroupBy(p => p.Domain).OrderByDescending(d => d.Count()).Take(3).Select(d => d.Key).ToList()))
            .OrderByDescending(t => t.Critical)
            .ThenByDescending(t => t.Risks)
            .ToList();

        return new PlatformRiskOverview(
            points.Count,
            points.Count(p => p.Severity >= 4 && p.Likelihood >= 4),
            perTenant.Count,
            allTenants.Count,
            perTenant,
            points.OrderByDescending(p => p.Index).Take(2000).ToList());
    }

    private static (int Severity, int Likelihood) SeverityLikelihood(ScoreRecord s)
    {
        // Prefer the severity/likelihood the KPI stamped into metadata.
        if (TryNum(s.Metadata, "severityValue", out var sev) || TryNum(s.Metadata, "severity", out sev))
        {
            TryNum(s.Metadata, "likelihood", out var like);
            return (Clamp(sev), Clamp(like == 0 ? 1 : like));
        }
        // Otherwise derive from the index value (severity ~ sqrt, likelihood ~ value/severity).
        var v = (double)s.Value;
        var severity = Clamp((int)Math.Round(Math.Sqrt(Math.Max(1, v))));
        var likelihood = Clamp((int)Math.Round(v / Math.Max(1, severity)));
        return (severity, likelihood);
    }

    private static bool TryNum(IReadOnlyDictionary<string, string>? meta, string key, out int value)
    {
        value = 0;
        if (meta is null) return false;
        var raw = meta.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Split('.')[0], out value);
    }

    private static int Clamp(int n) => Math.Max(1, Math.Min(5, n));

    private static readonly IEqualityComparer<(string, string)> TupleComparer =
        new TupleOrdinalComparer();

    private sealed class TupleOrdinalComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) o) =>
            HashCode.Combine(o.Item1.ToLowerInvariant(), o.Item2.ToLowerInvariant());
    }
}
