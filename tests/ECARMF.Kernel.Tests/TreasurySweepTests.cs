using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Treasury;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Treasury;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemorySweepAccountStore : ISweepAccountStore
{
    public List<SweepAccount> Items { get; } = [];

    public Task<SweepAccount?> GetAsync(string tenantId, string accountId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.TenantId == tenantId && a.AccountId == accountId));

    public Task<IReadOnlyList<SweepAccount>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SweepAccount>>(Items.Where(a => a.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<SweepAccount>> GetEnabledAllTenantsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SweepAccount>>(Items.Where(a => a.Enabled).ToList());

    public Task AddAsync(SweepAccount account, CancellationToken ct = default)
    { Items.Add(account); return Task.CompletedTask; }

    public Task UpdateAsync(SweepAccount account, CancellationToken ct = default)
    {
        var index = Items.FindIndex(a => a.TenantId == account.TenantId && a.AccountId == account.AccountId);
        if (index >= 0) Items[index] = account;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, string accountId, CancellationToken ct = default)
    { Items.RemoveAll(a => a.TenantId == tenantId && a.AccountId == accountId); return Task.CompletedTask; }
}

/// <summary>AI Treasury (Requirement 8): thresholds proposed Recommend-Only
/// from trailing balances, sweeps autonomous only against a standing
/// APPROVED threshold, payroll accounts alert and never sweep.</summary>
public class TreasurySweepTests
{
    private const string Tenant = "universal-dental";

    private readonly InMemorySweepAccountStore _accounts = new();
    private readonly InMemoryScoreStore _scores = new();
    private readonly InMemoryCapitalFlowStore _allocations = new();
    private readonly InMemoryNotificationStore _notifications = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TreasurySweepService _service;

    public TreasurySweepTests()
    {
        _service = new TreasurySweepService(_accounts, _scores, _allocations, _notifications, _audit);
    }

    private SweepAccount AddAccount(string id, string kind = SweepAccountKinds.Operating, decimal? approved = null)
    {
        var account = new SweepAccount
        {
            TenantId = Tenant, AccountId = id, Name = id, Institution = "Bank of America",
            Kind = kind, DestinationAccountId = "corporate-operating",
            ApprovedThreshold = approved, ApprovedBy = approved is null ? null : "treasury@ud",
            ApprovedAt = approved is null ? null : DateTimeOffset.UtcNow, CreatedBy = "admin"
        };
        _accounts.Items.Add(account);
        return account;
    }

    [Fact]
    public async Task Recalculation_proposes_but_never_applies()
    {
        AddAccount("oak-lawn-operating", approved: 20000m);
        foreach (var balance in new[] { 60000m, 64000m, 56000m, 60000m })
        {
            await _service.ObserveBalanceAsync(Tenant, "oak-lawn-operating", balance, "feed");
        }

        var proposals = await _service.RecalculateThresholdsAsync(Tenant, DateTimeOffset.UtcNow);

        Assert.Equal(1, proposals);
        var account = _accounts.Items.Single();
        Assert.Equal(30000m, account.ProposedThreshold); // mean 60000 x 0.5
        Assert.Equal(20000m, account.ApprovedThreshold); // standing threshold UNCHANGED
        Assert.Contains(_scores.Items, s => s.ScoreType == TreasurySweepService.ThresholdScoreType
            && s.Provenance == "AIGenerated");
        Assert.Contains(_notifications.Items, n => n.Target == "TreasuryOfficer");
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.TreasuryThresholdProposed);
    }

    [Fact]
    public async Task Insufficient_history_produces_no_guess()
    {
        AddAccount("elgin-operating");
        await _service.ObserveBalanceAsync(Tenant, "elgin-operating", 50000m, "feed");

        Assert.Equal(0, await _service.RecalculateThresholdsAsync(Tenant, DateTimeOffset.UtcNow));
        Assert.Null(_accounts.Items.Single().ProposedThreshold);
    }

    [Fact]
    public async Task Approval_activates_the_threshold_and_clears_the_proposal()
    {
        var account = AddAccount("oak-lawn-operating");
        account.ProposedThreshold = 30000m;

        var approved = await _service.ApproveThresholdAsync(Tenant, "oak-lawn-operating", "treasury@ud", null);

        Assert.Equal(30000m, approved.ApprovedThreshold);
        Assert.Equal("treasury@ud", approved.ApprovedBy);
        Assert.Null(approved.ProposedThreshold);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.TreasuryThresholdApproved);

        // A human override beats the AI proposal.
        approved.ProposedThreshold = 32000m;
        var overridden = await _service.ApproveThresholdAsync(Tenant, "oak-lawn-operating", "treasury@ud", 25000m);
        Assert.Equal(25000m, overridden.ApprovedThreshold);
    }

    [Fact]
    public async Task Overage_above_a_standing_threshold_sweeps_autonomously_with_full_reasoning()
    {
        AddAccount("oak-lawn-operating", approved: 20000m);

        var result = await _service.ObserveBalanceAsync(Tenant, "oak-lawn-operating", 27500m, "feed");

        Assert.True(result.SweepExecuted);
        Assert.Equal(7500m, result.SweepAmount);
        var sweep = Assert.Single(_allocations.Items);
        Assert.Equal(AutonomyTier.Autonomous, sweep.Tier);
        Assert.Equal("AutoExecuted", sweep.Status);
        Assert.Equal("corporate-operating", sweep.TargetReference);
        Assert.Equal(7500m, sweep.Amount);
        Assert.Contains("20,000", sweep.Reasoning); // threshold in effect is stated
        Assert.NotEmpty(sweep.SupportingScoreRecordIds);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.TreasurySweepExecuted);
    }

    [Fact]
    public async Task No_approved_threshold_means_no_sweep_ever()
    {
        var account = AddAccount("pulaski-operating");
        account.ProposedThreshold = 15000m; // proposed but never approved

        var result = await _service.ObserveBalanceAsync(Tenant, "pulaski-operating", 90000m, "feed");

        Assert.False(result.SweepExecuted);
        Assert.Empty(_allocations.Items);
    }

    [Fact]
    public async Task Payroll_accounts_alert_and_never_sweep()
    {
        AddAccount("oak-lawn-payroll", kind: SweepAccountKinds.Payroll, approved: 40000m);

        var result = await _service.ObserveBalanceAsync(Tenant, "oak-lawn-payroll", 65000m, "feed");

        Assert.False(result.SweepExecuted);
        Assert.True(result.PayrollAlertRaised);
        Assert.Empty(_allocations.Items); // structurally impossible to sweep payroll
        Assert.Contains(_notifications.Items, n => n.Severity == "Warning" && n.Message.Contains("No sweep"));
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.PayrollBalanceFlagged);
    }

    [Fact]
    public async Task Redundant_proposals_are_suppressed_by_the_deadband()
    {
        AddAccount("oak-lawn-operating", approved: 30000m);
        foreach (var balance in new[] { 60000m, 61000m, 59000m })
        {
            await _service.ObserveBalanceAsync(Tenant, "oak-lawn-operating", balance, "feed");
        }

        // Mean 60000 x 0.5 = 30000 == standing threshold: nothing to say.
        Assert.Equal(0, await _service.RecalculateThresholdsAsync(Tenant, DateTimeOffset.UtcNow));
    }
}
