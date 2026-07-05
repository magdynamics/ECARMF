using System.Text;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Billing;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class RecordingBillingService : IBillingService
{
    private readonly InMemoryStatementStore _statements;
    public RecordingBillingService(InMemoryStatementStore statements) => _statements = statements;

    public async Task<BillingStatement> GenerateStatementAsync(
        string tenantId, string planId, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        string generatedBy, CancellationToken ct = default)
    {
        var statement = new BillingStatement
        {
            TenantId = tenantId, PlanId = planId,
            PeriodStart = periodStart, PeriodEnd = periodEnd, Total = 100m
        };
        await _statements.AddAsync(statement, ct);
        return statement;
    }
}

public class InMemoryStatementStore : IBillingStatementStore
{
    public List<BillingStatement> Items { get; } = [];
    public Task AddAsync(BillingStatement statement, CancellationToken ct = default)
    { Items.Add(statement); return Task.CompletedTask; }
    public Task<IReadOnlyList<BillingStatement>> GetForTenantAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BillingStatement>>(Items.Where(s => s.TenantId == tenantId).Take(limit).ToList());
}

public class FakePlanStore : IBillingPlanStore
{
    public Task<BillingPlan?> GetAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult<BillingPlan?>(new BillingPlan { PlanId = planId, Name = planId });
    public Task<IReadOnlyList<BillingPlan>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BillingPlan>>([]);
    public Task AddAsync(BillingPlan plan, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnsureDefaultPlanAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Hardening batch: month-close billing for everyone exactly once,
/// audit CSV an examiner can open, peer stats that protect anonymity.</summary>
public class HardeningBatchTests
{
    [Fact]
    public async Task Monthly_billing_closes_every_active_tenant_exactly_once()
    {
        var tenants = new FakeTenantDirectory();
        tenants.Profiles["a"] = new TenantProfile { TenantId = "a", Name = "A", Status = "Active", BillingPlanId = "premium" };
        tenants.Profiles["b"] = new TenantProfile { TenantId = "b", Name = "B", Status = "Active" };
        tenants.Profiles["c"] = new TenantProfile { TenantId = "c", Name = "C", Status = "Suspended" };
        var statements = new InMemoryStatementStore();
        var service = new MonthlyBillingService(
            tenants, new RecordingBillingService(statements), statements, new FakePlanStore());
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(2, await service.EnsureMonthlyStatementsAsync(now)); // a + b, not suspended c
        Assert.Equal(0, await service.EnsureMonthlyStatementsAsync(now)); // idempotent

        Assert.Equal("premium", statements.Items.Single(s => s.TenantId == "a").PlanId);
        Assert.Equal(MonthlyBillingService.DefaultPlanId, statements.Items.Single(s => s.TenantId == "b").PlanId);
        Assert.DoesNotContain(statements.Items, s => s.TenantId == "c");
    }

    [Fact]
    public void Audit_csv_escapes_and_flattens_for_an_examiner()
    {
        var csv = Encoding.UTF8.GetString(AuditCsvExporter.ToCsv(
        [
            new AuditEntry
            {
                TenantId = "t", Category = "RuleEvaluated", Actor = "system:flywheel",
                Summary = "Rule \"R-1\" fired,\nwith newline",
                Detail = new Dictionary<string, string> { ["ruleId"] = "R-1", ["outcome"] = "Flagged" }
            }
        ]));

        var lines = csv.Trim().Split('\n');
        Assert.StartsWith("OccurredAtUtc,Category,Actor,Summary", lines[0]);
        Assert.Equal(2, lines.Length); // the newline in the summary must not add a row
        Assert.Contains("\"Rule \"\"R-1\"\" fired, with newline\"", lines[1]);
        Assert.Contains("ruleId=R-1; outcome=Flagged", lines[1]);
    }

    [Fact]
    public async Task Peer_benchmark_withholds_below_minimum_peers()
    {
        var scores = new InMemoryScoreStore();
        scores.Items.Add(new ScoreRecord { TenantId = "me", ScoreType = "GPPercent", Value = 0.22m });
        scores.Items.Add(new ScoreRecord { TenantId = "peer1", ScoreType = "GPPercent", Value = 0.30m });
        scores.Items.Add(new ScoreRecord { TenantId = "peer2", ScoreType = "GPPercent", Value = 0.28m });
        var service = new PeerBenchmarkService(scores);

        var result = await service.CompareAsync("me", "GPPercent");

        Assert.False(result.Available); // only 2 peers: median would identify them
        Assert.Equal(0.22m, result.YourAverage);
        Assert.Null(result.PeerMedian);
    }

    [Fact]
    public async Task Peer_benchmark_aggregates_one_number_per_peer()
    {
        var scores = new InMemoryScoreStore();
        scores.Items.Add(new ScoreRecord { TenantId = "me", ScoreType = "GPPercent", Value = 0.22m });
        // peer1 has many observations — still one contribution to the distribution.
        scores.Items.Add(new ScoreRecord { TenantId = "peer1", ScoreType = "GPPercent", Value = 0.20m });
        scores.Items.Add(new ScoreRecord { TenantId = "peer1", ScoreType = "GPPercent", Value = 0.40m });
        scores.Items.Add(new ScoreRecord { TenantId = "peer2", ScoreType = "GPPercent", Value = 0.27m });
        scores.Items.Add(new ScoreRecord { TenantId = "peer3", ScoreType = "GPPercent", Value = 0.33m });
        var service = new PeerBenchmarkService(scores);

        var result = await service.CompareAsync("me", "GPPercent");

        Assert.True(result.Available);
        Assert.Equal(3, result.PeerCount);
        Assert.Equal(0.30m, result.PeerMedian); // peer averages 0.30, 0.27, 0.33 -> median 0.30
        Assert.Equal(0.22m, result.YourAverage);
    }
}
