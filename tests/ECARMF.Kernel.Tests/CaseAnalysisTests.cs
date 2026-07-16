using ECARMF.Kernel.Application.Cases;
using ECARMF.Kernel.Domain.Cases;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Transactions;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Cases are compared side by side with the same measures as period
/// analysis; the metrics must count only records filed under each case.</summary>
public class CaseAnalysisTests
{
    private const string Tenant = "t1";

    private static (CaseAnalysisService Service, InMemoryCaseStore Cases,
        InMemoryTransactionStore Records, InMemoryOutcomeStore Outcomes, InMemoryScoreStore Scores) Build()
    {
        var cases = new InMemoryCaseStore();
        var records = new InMemoryTransactionStore();
        var outcomes = new InMemoryOutcomeStore();
        var scores = new InMemoryScoreStore();
        return (new CaseAnalysisService(cases, records, outcomes, scores), cases, records, outcomes, scores);
    }

    private static Transaction Record(string? caseId) => new()
    {
        TenantId = Tenant, TransactionType = "Thing", SubmittedBy = "u",
        ReceivedAt = DateTimeOffset.UtcNow, CaseId = caseId
    };

    [Fact]
    public async Task Metrics_count_only_the_records_filed_under_each_case()
    {
        var (service, cases, records, outcomes, scores) = Build();
        cases.Items.Add(new Case { TenantId = Tenant, CaseId = "audit", Name = "Audit" });
        cases.Items.Add(new Case { TenantId = Tenant, CaseId = "empty", Name = "Empty" });

        // 3 records in "audit": 1 rejected, 1 flagged, 2 distinct controls, 1 scored.
        var r1 = Record("audit"); var r2 = Record("audit"); var r3 = Record("audit");
        records.Items.AddRange([r1, r2, r3, Record(null), Record("other")]);
        outcomes.Items.Add(new TransactionOutcome { TenantId = Tenant, TransactionId = r1.TransactionId, Outcome = "Rejected", RuleId = "C-1" });
        outcomes.Items.Add(new TransactionOutcome { TenantId = Tenant, TransactionId = r2.TransactionId, Outcome = "Flagged", RuleId = "C-2" });
        scores.Items.Add(new ScoreRecord { TenantId = Tenant, ScoreType = "KPIActual", Value = 12, CorrelationId = r3.TransactionId });

        var metrics = await service.CompareAsync(Tenant);

        var audit = metrics.Single(m => m.CaseId == "audit");
        Assert.Equal(3, audit.Records);
        Assert.Equal(1, audit.Rejected);
        Assert.Equal(1, audit.Flagged);
        Assert.Equal(2, audit.ControlsFired);
        Assert.Equal(12m, audit.AvgScore);

        var empty = metrics.Single(m => m.CaseId == "empty");
        Assert.Equal(0, empty.Records);
        Assert.Equal(0m, empty.AvgScore);
    }

    [Fact]
    public async Task Cases_are_ordered_by_record_volume_descending()
    {
        var (service, cases, records, _, _) = Build();
        cases.Items.Add(new Case { TenantId = Tenant, CaseId = "small", Name = "Small" });
        cases.Items.Add(new Case { TenantId = Tenant, CaseId = "big", Name = "Big" });
        records.Items.Add(Record("small"));
        for (var i = 0; i < 4; i++) records.Items.Add(Record("big"));

        var metrics = await service.CompareAsync(Tenant);

        Assert.Equal("big", metrics[0].CaseId);
        Assert.Equal("small", metrics[1].CaseId);
    }

    [Fact]
    public async Task Pages_past_the_stores_row_cap_when_a_case_is_large()
    {
        var (service, cases, records, _, _) = Build();
        cases.Items.Add(new Case { TenantId = Tenant, CaseId = "huge", Name = "Huge" });
        for (var i = 0; i < 260; i++) records.Items.Add(Record("huge"));

        var metrics = await service.CompareAsync(Tenant);

        Assert.Equal(260, metrics.Single().Records); // > the 200-row store cap
    }

    [Fact]
    public async Task No_cases_means_an_empty_comparison_not_an_error()
    {
        var (service, _, _, _, _) = Build();
        Assert.Empty(await service.CompareAsync(Tenant));
    }
}
