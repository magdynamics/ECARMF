using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;

namespace ECARMF.Kernel.Application.Analytics;

public sealed record PeriodMetrics(
    string Label,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Records,
    int Rejected,
    int Flagged,
    int ControlsFired,
    decimal AvgScore);

public sealed record PeriodDelta(string Metric, decimal Current, decimal Previous, decimal ChangePct, bool Improved);

public sealed record PeriodComparison(
    PeriodMetrics? Current,
    PeriodMetrics? Previous,
    IReadOnlyList<PeriodDelta> Deltas,
    IReadOnlyList<string> Recommendations);

public sealed record PeriodAnalysis(
    string Granularity,
    IReadOnlyList<PeriodMetrics> Periods,
    PeriodComparison Comparison);

public interface IPeriodAnalysisService
{
    /// <param name="unitRef">Narrows the analysis to one organizational unit
    /// (its own records plus tenant-wide ones); null = the whole tenant.</param>
    Task<PeriodAnalysis> AnalyzeAsync(
        string tenantId, string granularity, int count, string? unitRef = null, CancellationToken ct = default);
}

/// <summary>
/// Answers "how is this tenant doing this period versus last?" — buckets the
/// tenant's records (and their outcomes and KPI scores) into calendar periods
/// by receive time, then compares the two most recent periods and turns the
/// deltas into plain-language recommendations. Reads only what the kernel
/// already records; a period is a date range, not a stored entity.
/// </summary>
public class PeriodAnalysisService : IPeriodAnalysisService
{
    private readonly ITransactionStore _records;
    private readonly IOutcomeStore _outcomes;
    private readonly IScoreStore _scores;

    public PeriodAnalysisService(ITransactionStore records, IOutcomeStore outcomes, IScoreStore scores)
    {
        _records = records;
        _outcomes = outcomes;
        _scores = scores;
    }

    public async Task<PeriodAnalysis> AnalyzeAsync(
        string tenantId, string granularity, int count, string? unitRef = null, CancellationToken ct = default)
    {
        var gran = string.Equals(granularity, "quarter", StringComparison.OrdinalIgnoreCase) ? "quarter" : "month";
        count = Math.Clamp(count, 2, 12);

        // Period windows, oldest → newest.
        var windows = Enumerable.Range(0, count)
            .Select(i => PeriodWindow(DateTimeOffset.UtcNow, gran, count - 1 - i))
            .ToList();
        var windowStart = windows[0].Start;

        // The record store caps Take per call, so page through the window.
        var records = new List<Domain.Transactions.Transaction>();
        for (var skip = 0; skip < 20000; skip += 200)
        {
            var (batch, _) = await _records.QueryAsync(
                new TransactionQuery(tenantId, null, null, null, null, windowStart, DateTimeOffset.UtcNow, skip, 200,
                    UnitRef: unitRef), ct);
            records.AddRange(batch);
            if (batch.Count < 200) break;
        }

        var ids = records.Select(r => r.TransactionId).ToList();
        var outcomesById = (await _outcomes.GetForTransactionsAsync(tenantId, ids, ct))
            .GroupBy(o => o.TransactionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // KPI actuals, keyed by the record that produced them (correlationId).
        var scoresByRecord = (await _scores.GetRecentAsync(tenantId, 8000, "KPIActual", ct))
            .GroupBy(s => s.CorrelationId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Value).ToList());

        // Accumulators per period index.
        var recs = new int[count];
        var rej = new int[count];
        var flag = new int[count];
        var controls = new HashSet<string>[count];
        var scoreSum = new decimal[count];
        var scoreN = new int[count];
        for (var i = 0; i < count; i++) controls[i] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in records)
        {
            var idx = windows.FindIndex(w => r.ReceivedAt >= w.Start && r.ReceivedAt < w.End);
            if (idx < 0) continue;
            recs[idx]++;

            if (outcomesById.TryGetValue(r.TransactionId, out var outs))
            {
                if (outs.Any(o => string.Equals(o.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))) rej[idx]++;
                if (outs.Any(o => string.Equals(o.Outcome, "Flagged", StringComparison.OrdinalIgnoreCase))) flag[idx]++;
                foreach (var o in outs) if (!string.IsNullOrWhiteSpace(o.RuleId)) controls[idx].Add(o.RuleId);
            }

            if (scoresByRecord.TryGetValue(r.TransactionId, out var vals) && vals.Count > 0)
            {
                scoreSum[idx] += vals.Sum();
                scoreN[idx] += vals.Count;
            }
        }

        var periods = new List<PeriodMetrics>();
        for (var i = 0; i < count; i++)
        {
            periods.Add(new PeriodMetrics(
                windows[i].Label, windows[i].Start, windows[i].End,
                recs[i], rej[i], flag[i], controls[i].Count,
                scoreN[i] > 0 ? Math.Round(scoreSum[i] / scoreN[i], 2) : 0));
        }

        return new PeriodAnalysis(gran, periods, Compare(periods));
    }

    private static PeriodComparison Compare(IReadOnlyList<PeriodMetrics> periods)
    {
        var current = periods.Count >= 1 ? periods[^1] : null;
        var previous = periods.Count >= 2 ? periods[^2] : null;
        if (current is null || previous is null)
            return new PeriodComparison(current, previous, [], ["Not enough history yet — a second period is needed to compare."]);

        // For rejections/flags/risk, lower is better; for records (activity), higher.
        var deltas = new List<PeriodDelta>
        {
            Delta("Records", current.Records, previous.Records, higherIsBetter: true),
            Delta("Rejected", current.Rejected, previous.Rejected, higherIsBetter: false),
            Delta("Flagged", current.Flagged, previous.Flagged, higherIsBetter: false),
            Delta("Avg risk score", current.AvgScore, previous.AvgScore, higherIsBetter: false),
        };

        var recs = new List<string>();
        var rejD = deltas[1]; var flagD = deltas[2]; var scoreD = deltas[3]; var recD = deltas[0];
        if (rejD.ChangePct >= 15) recs.Add($"Rejections rose {rejD.ChangePct:0}% — review the most-triggered control this period.");
        else if (rejD.ChangePct <= -15) recs.Add($"Rejections fell {Math.Abs(rejD.ChangePct):0}% — controls are catching fewer issues; confirm it's real improvement, not lower volume.");
        if (flagD.ChangePct >= 15) recs.Add($"Flags rose {flagD.ChangePct:0}% — work down the flagged queue before month close.");
        if (scoreD.Current > 0 && scoreD.ChangePct >= 10) recs.Add($"Average risk score climbed {scoreD.ChangePct:0}% — check the risk register for new high-severity items.");
        if (recD.ChangePct <= -25) recs.Add($"Activity dropped {Math.Abs(recD.ChangePct):0}% — confirm data feeds and integrations are running.");
        if (recs.Count == 0) recs.Add("Key indicators are stable or improving versus last period. Keep the current controls in place.");

        return new PeriodComparison(current, previous, deltas, recs);
    }

    private static PeriodDelta Delta(string metric, decimal current, decimal previous, bool higherIsBetter)
    {
        var change = previous == 0 ? (current == 0 ? 0 : 100) : Math.Round((current - previous) / previous * 100, 1);
        var improved = higherIsBetter ? current >= previous : current <= previous;
        return new PeriodDelta(metric, current, previous, change, improved);
    }

    private static (DateTimeOffset Start, DateTimeOffset End, string Label) PeriodWindow(
        DateTimeOffset anchor, string gran, int back)
    {
        if (gran == "quarter")
        {
            var q = (anchor.Month - 1) / 3;
            var baseStart = new DateTimeOffset(anchor.Year, q * 3 + 1, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-3 * back);
            return (baseStart, baseStart.AddMonths(3), $"Q{(baseStart.Month - 1) / 3 + 1} {baseStart.Year}");
        }
        var start = new DateTimeOffset(anchor.Year, anchor.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-back);
        return (start, start.AddMonths(1), start.ToString("MMM yyyy"));
    }
}
