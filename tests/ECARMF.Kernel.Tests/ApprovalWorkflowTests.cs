using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Transactions;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Dual-approval control: a flagged record is held until a second
/// approver — different from the submitter — approves or rejects it.</summary>
public class ApprovalWorkflowTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryTransactionStore _transactions = new();
    private readonly InMemoryOutcomeStore _outcomes = new();
    private readonly InMemoryApprovalStore _approvals = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();
    private readonly InMemoryAuditLog _audit = new();

    private ApprovalService CreateService() =>
        new(_transactions, _outcomes, _approvals, _registries, _bus, _audit);

    private Guid SeedFlaggedRecord(string submittedBy = "treasurer@example.com")
    {
        var transaction = new Transaction
        {
            TenantId = Tenant,
            TransactionType = "withdrawal",
            SubmittedBy = submittedBy,
            Payload = new Dictionary<string, string> { ["amount"] = "60000", ["ventureId"] = "V-001" }
        };
        _transactions.Items.Add(transaction);
        _outcomes.Items.Add(new TransactionOutcome
        {
            TenantId = Tenant,
            TransactionId = transaction.TransactionId,
            EventName = KernelEventNames.RecordReceived,
            Outcome = KernelOutcomes.Flagged,
            Reason = "held",
            RuleId = "TREASURY-R-001",
            PackageId = "ecarmf.treasury-controls",
            PackageVersion = "1.0.0"
        });
        return transaction.TransactionId;
    }

    [Fact]
    public async Task Second_approver_releases_a_flagged_record()
    {
        var id = SeedFlaggedRecord();
        var service = CreateService();

        var result = await service.DecideAsync(new ApprovalSubmission(
            Tenant, id, "cfo@example.com", ApprovalVerdict.Approve, "verified with venture manager"));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(KernelOutcomes.Approved, result.Outcome!.Outcome);
        Assert.Contains("cfo@example.com", result.Outcome.Reason);
        // The releasing outcome stays traceable to the rule that placed the hold.
        Assert.Equal("TREASURY-R-001", result.Outcome.RuleId);
        // Decision + outcome are audited.
        var trail = await _audit.GetByCorrelationAsync(Tenant, id);
        Assert.Contains(trail, a => a.Category == AuditCategories.ApprovalRecorded);
        Assert.Contains(trail, a => a.Category == AuditCategories.OutcomeRecorded);
    }

    [Fact]
    public async Task Rejection_records_a_rejected_outcome()
    {
        var id = SeedFlaggedRecord();
        var service = CreateService();

        var result = await service.DecideAsync(new ApprovalSubmission(
            Tenant, id, "cfo@example.com", ApprovalVerdict.Reject, "insufficient justification"));

        Assert.True(result.Success);
        Assert.Equal(KernelOutcomes.Rejected, result.Outcome!.Outcome);
    }

    [Fact]
    public async Task Approver_must_differ_from_submitter()
    {
        var id = SeedFlaggedRecord(submittedBy: "treasurer@example.com");
        var service = CreateService();

        var result = await service.DecideAsync(new ApprovalSubmission(
            Tenant, id, "TREASURER@example.com", ApprovalVerdict.Approve, null));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("differ from the transaction submitter"));
    }

    [Fact]
    public async Task Only_flagged_records_can_be_decided()
    {
        var transaction = new Transaction { TenantId = Tenant, SubmittedBy = "a@x.com" };
        _transactions.Items.Add(transaction);
        _outcomes.Items.Add(new TransactionOutcome
        {
            TenantId = Tenant,
            TransactionId = transaction.TransactionId,
            Outcome = KernelOutcomes.Approved,
            Reason = "auto"
        });
        var service = CreateService();

        var result = await service.DecideAsync(new ApprovalSubmission(
            Tenant, transaction.TransactionId, "b@x.com", ApprovalVerdict.Approve, null));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("not awaiting approval"));
    }

    [Fact]
    public async Task A_record_is_decided_at_most_once()
    {
        var id = SeedFlaggedRecord();
        var service = CreateService();
        await service.DecideAsync(new ApprovalSubmission(Tenant, id, "cfo@example.com", ApprovalVerdict.Approve, null));

        var second = await service.DecideAsync(new ApprovalSubmission(
            Tenant, id, "cro@example.com", ApprovalVerdict.Reject, null));

        Assert.False(second.Success);
        Assert.Contains(second.Errors, e => e.Contains("already decided"));
    }

    [Fact]
    public async Task Unknown_record_is_rejected()
    {
        var service = CreateService();

        var result = await service.DecideAsync(new ApprovalSubmission(
            Tenant, Guid.NewGuid(), "cfo@example.com", ApprovalVerdict.Approve, null));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }
}
