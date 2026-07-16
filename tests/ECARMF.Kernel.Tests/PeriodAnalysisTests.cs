using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Transactions;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Period analysis answers "how are we doing vs last period?" — its
/// bucketing, paging past the store's row cap, delta directionality, and
/// recommendation thresholds all shape operator decisions.</summary>
public class PeriodAnalysisTests
{
    private const string Tenant = "t1";

    private static (PeriodAnalysisService Service, InMemoryTransactionStore Records,
        InMemoryOutcomeStore Outcomes, InMemoryScoreStore Scores) Build()
    {
        var records = new InMemoryTransactionStore();
        var outcomes = new InMemoryOutcomeStore();
        var scores = new InMemoryScoreStore();
        return (new PeriodAnalysisService(records, outcomes, scores), records, outcomes, scores);
    }

    private static Transaction Record(DateTimeOffset at, string type = "Thing") => new()
    {
        TenantId = Tenant, TransactionType = type, SubmittedBy = "u", ReceivedAt = at
    };

    private static DateTimeOffset MidMonth(int monthsBack)
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-monthsBack);
        return start.AddDays(14);
    }

    [Fact]
    public async Task Buckets_records_into_the_correct_calendar_months()
    {
        var (service, records, _, _) = Build();
        for (var i = 0; i < 3; i++) records.Items.Add(Record(MidMonth(0)));
        for (var i = 0; i < 5; i++) records.Items.Add(Record(MidMonth(1)));
        for (var i = 0; i < 2; i++) records.Items.Add(Record(MidMonth(2)));

        var analysis = await service.AnalyzeAsync(Tenant, "month", 6);

        Assert.Equal(6, analysis.Periods.Count);
        Assert.Equal(3, analysis.Periods[^1].Records); // current
        Assert.Equal(5, analysis.Periods[^2].Records); // previous
        Assert.Equal(2, analysis.Periods[^3].Records);
        Assert.Equal(0, analysis.Periods[0].Records);  // oldest, empty
    }

    [Fact]
    public async Task Pages_past_the_stores_200_row_cap_so_every_record_is_counted()
    {
        var (service, records, _, _) = Build();
        // The in-memory fake mirrors the EF store's Take clamp of 200; a
        // non-paging implementation would report at most 200 of these 450.
        for (var i = 0; i < 450; i++) records.Items.Add(Record(MidMonth(0).AddMinutes(i)));

        var analysis = await service.AnalyzeAsync(Tenant, "month", 3);

        Assert.Equal(450, analysis.Periods[^1].Records);
    }

    [Fact]
    public async Task Deltas_treat_falling_rejections_as_improvement_and_falling_activity_as_not()
    {
        var (service, records, outcomes, _) = Build();
        // Previous month: 10 records, 6 rejected. Current: 4 records, 1 rejected.
        for (var i = 0; i < 10; i++)
        {
            var r = Record(MidMonth(1).AddMinutes(i));
            records.Items.Add(r);
            if (i < 6) outcomes.Items.Add(new TransactionOutcome
            {
                TenantId = Tenant, TransactionId = r.TransactionId, Outcome = "Rejected", RuleId = $"R-{i}"
            });
        }
        for (var i = 0; i < 4; i++)
        {
            var r = Record(MidMonth(0).AddMinutes(i));
            records.Items.Add(r);
            if (i < 1) outcomes.Items.Add(new TransactionOutcome
            {
                TenantId = Tenant, TransactionId = r.TransactionId, Outcome = "Rejected", RuleId = "R-x"
            });
        }

        var c = (await service.AnalyzeAsync(Tenant, "month", 3)).Comparison;

        var recordsDelta = c.Deltas.Single(d => d.Metric == "Records");
        var rejectedDelta = c.Deltas.Single(d => d.Metric == "Rejected");
        Assert.False(recordsDelta.Improved);  // activity fell — not an improvement
        Assert.True(rejectedDelta.Improved);  // rejections fell — improvement
        // -25% activity drop and -15% rejection drop both trip recommendations.
        Assert.Contains(c.Recommendations, r => r.Contains("Activity dropped", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(c.Recommendations, r => r.Contains("Rejections fell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stable_periods_produce_the_keep_course_recommendation()
    {
        var (service, records, _, _) = Build();
        for (var i = 0; i < 5; i++) records.Items.Add(Record(MidMonth(0).AddMinutes(i)));
        for (var i = 0; i < 5; i++) records.Items.Add(Record(MidMonth(1).AddMinutes(i)));

        var c = (await service.AnalyzeAsync(Tenant, "month", 3)).Comparison;

        Assert.Single(c.Recommendations);
        Assert.Contains("stable", c.Recommendations[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kpi_scores_join_by_correlation_id_into_the_period_average()
    {
        var (service, records, _, scores) = Build();
        var r1 = Record(MidMonth(0));
        var r2 = Record(MidMonth(0).AddHours(1));
        records.Items.AddRange([r1, r2]);
        scores.Items.Add(new ScoreRecord { TenantId = Tenant, ScoreType = "KPIActual", Value = 10, CorrelationId = r1.TransactionId });
        scores.Items.Add(new ScoreRecord { TenantId = Tenant, ScoreType = "KPIActual", Value = 30, CorrelationId = r2.TransactionId });

        var analysis = await service.AnalyzeAsync(Tenant, "month", 2);

        Assert.Equal(20m, analysis.Periods[^1].AvgScore);
    }

    [Fact]
    public async Task Quarter_granularity_labels_and_buckets_by_quarter()
    {
        var (service, records, _, _) = Build();
        records.Items.Add(Record(DateTimeOffset.UtcNow));

        var analysis = await service.AnalyzeAsync(Tenant, "quarter", 4);

        Assert.Equal("quarter", analysis.Granularity);
        Assert.Equal(4, analysis.Periods.Count);
        var current = analysis.Periods[^1];
        Assert.Matches(@"^Q[1-4] \d{4}$", current.Label);
        Assert.Equal(1, current.Records);
    }
}
