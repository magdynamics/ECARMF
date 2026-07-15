using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Cases;

namespace ECARMF.Kernel.Application.Cases;

/// <summary>Persistence for cases/projects. Tenant-scoped.</summary>
public interface ICaseStore
{
    Task<IReadOnlyList<Case>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<Case?> GetAsync(string tenantId, string caseId, CancellationToken ct = default);
    Task AddAsync(Case c, CancellationToken ct = default);
    Task UpdateAsync(Case c, CancellationToken ct = default);
}

/// <summary>A case with the metrics of the records filed under it — the same
/// measures Period Analysis uses, so cases are comparable to each other.</summary>
public sealed record CaseMetrics(
    string CaseId,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<string> Skills,
    int Records,
    int Rejected,
    int Flagged,
    int ControlsFired,
    decimal AvgScore,
    DateTimeOffset CreatedAt);

public interface ICaseAnalysisService
{
    /// <summary>Every case in the tenant with its metrics, for side-by-side
    /// comparison.</summary>
    Task<IReadOnlyList<CaseMetrics>> CompareAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Measures each case by the records filed under it and lets cases be compared
/// — "how is this case doing versus the others?". Reads only what the kernel
/// already records; the case is a label on records.
/// </summary>
public class CaseAnalysisService : ICaseAnalysisService
{
    private readonly ICaseStore _cases;
    private readonly ITransactionStore _records;
    private readonly IOutcomeStore _outcomes;
    private readonly IScoreStore _scores;

    public CaseAnalysisService(ICaseStore cases, ITransactionStore records, IOutcomeStore outcomes, IScoreStore scores)
    {
        _cases = cases;
        _records = records;
        _outcomes = outcomes;
        _scores = scores;
    }

    public async Task<IReadOnlyList<CaseMetrics>> CompareAsync(string tenantId, CancellationToken ct = default)
    {
        var cases = await _cases.GetAllAsync(tenantId, ct);
        if (cases.Count == 0) return [];

        // KPI actuals for the whole tenant, keyed by record — reused per case.
        var scoresByRecord = (await _scores.GetRecentAsync(tenantId, 8000, "KPIActual", ct))
            .GroupBy(s => s.CorrelationId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Value).ToList());

        var result = new List<CaseMetrics>();
        foreach (var c in cases)
        {
            // Records filed under this case (the store caps Take, so page).
            var records = new List<Domain.Transactions.Transaction>();
            for (var skip = 0; skip < 20000; skip += 200)
            {
                var (batch, _) = await _records.QueryAsync(
                    new TransactionQuery(tenantId, Skip: skip, Take: 200, CaseId: c.CaseId), ct);
                records.AddRange(batch);
                if (batch.Count < 200) break;
            }

            var outcomesById = (await _outcomes.GetForTransactionsAsync(tenantId, records.Select(r => r.TransactionId).ToList(), ct))
                .GroupBy(o => o.TransactionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int rej = 0, flag = 0; decimal scoreSum = 0; int scoreN = 0;
            var controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                if (outcomesById.TryGetValue(r.TransactionId, out var outs))
                {
                    if (outs.Any(o => string.Equals(o.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))) rej++;
                    if (outs.Any(o => string.Equals(o.Outcome, "Flagged", StringComparison.OrdinalIgnoreCase))) flag++;
                    foreach (var o in outs) if (!string.IsNullOrWhiteSpace(o.RuleId)) controls.Add(o.RuleId);
                }
                if (scoresByRecord.TryGetValue(r.TransactionId, out var vals) && vals.Count > 0)
                {
                    scoreSum += vals.Sum();
                    scoreN += vals.Count;
                }
            }

            result.Add(new CaseMetrics(
                c.CaseId, c.Name, c.Description, c.Status, c.Skills,
                records.Count, rej, flag, controls.Count,
                scoreN > 0 ? Math.Round(scoreSum / scoreN, 2) : 0, c.CreatedAt));
        }

        return result.OrderByDescending(m => m.Records).ToList();
    }
}
