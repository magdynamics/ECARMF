using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

public sealed record ApprovalSubmission(
    string TenantId,
    Guid TransactionId,
    string Approver,
    ApprovalVerdict Verdict,
    string? Comment);

public sealed record ApprovalResult(
    bool Success,
    IReadOnlyList<string> Errors,
    TransactionOutcome? Outcome)
{
    public static ApprovalResult Ok(TransactionOutcome outcome) => new(true, [], outcome);
    public static ApprovalResult Fail(params string[] errors) => new(false, errors, null);
}

public interface IApprovalService
{
    Task<ApprovalResult> DecideAsync(ApprovalSubmission submission, CancellationToken ct = default);
}

/// <summary>
/// Kernel mechanism for the dual-approval control (RequireDualApproval).
/// A flagged transaction is held until a second approver — who must differ
/// from the submitter — approves or rejects it. The decision is append-only,
/// audited before its consequences are published, and the resulting outcome
/// stays traceable to the decision and the rule that placed the hold.
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly ITransactionStore _transactions;
    private readonly IOutcomeStore _outcomes;
    private readonly IApprovalStore _approvals;
    private readonly ITenantRegistryProvider _registries;
    private readonly IKernelEventBus _bus;
    private readonly IAuditLog _audit;
    private readonly Flywheel.IAILearningFeedbackService _feedback;

    public ApprovalService(
        ITransactionStore transactions,
        IOutcomeStore outcomes,
        IApprovalStore approvals,
        ITenantRegistryProvider registries,
        IKernelEventBus bus,
        IAuditLog audit,
        Flywheel.IAILearningFeedbackService feedback)
    {
        _transactions = transactions;
        _outcomes = outcomes;
        _approvals = approvals;
        _registries = registries;
        _bus = bus;
        _audit = audit;
        _feedback = feedback;
    }

    public async Task<ApprovalResult> DecideAsync(ApprovalSubmission submission, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(submission.Approver))
        {
            return ApprovalResult.Fail("approver is required.");
        }

        var transaction = await _transactions.GetByIdAsync(submission.TenantId, submission.TransactionId, ct);
        if (transaction is null)
        {
            return ApprovalResult.Fail($"Transaction '{submission.TransactionId}' does not exist for this tenant.");
        }

        if (string.Equals(transaction.SubmittedBy, submission.Approver, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalResult.Fail("The second approver must differ from the transaction submitter.");
        }

        var existing = await _approvals.GetForTransactionAsync(submission.TenantId, submission.TransactionId, ct);
        if (existing.Count > 0)
        {
            return ApprovalResult.Fail(
                $"Transaction already decided by '{existing[0].Approver}' ({existing[0].Verdict}) at {existing[0].DecidedAt:O}.");
        }

        var history = await _outcomes.GetForTransactionsAsync(
            submission.TenantId, [submission.TransactionId], ct);
        var latest = history.LastOrDefault();
        if (latest is null || !string.Equals(latest.Outcome, KernelOutcomes.Flagged, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalResult.Fail(
                $"Record is not awaiting approval (current outcome: {latest?.Outcome ?? "none"}).");
        }

        var decision = new ApprovalDecision
        {
            TenantId = submission.TenantId,
            TransactionId = submission.TransactionId,
            Approver = submission.Approver,
            Verdict = submission.Verdict,
            Comment = submission.Comment,
            DecidedAt = DateTimeOffset.UtcNow
        };

        // Durable + audited before any consequence is visible downstream.
        await _approvals.AppendAsync(decision, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = submission.TenantId,
            CorrelationId = submission.TransactionId,
            Category = AuditCategories.ApprovalRecorded,
            Actor = decision.Approver,
            Summary = $"Dual-approval decision '{decision.Verdict}' by '{decision.Approver}'.",
            Detail = new Dictionary<string, string>
            {
                ["approver"] = decision.Approver,
                ["verdict"] = decision.Verdict.ToString(),
                ["comment"] = decision.Comment ?? string.Empty,
                ["heldByRuleId"] = latest.RuleId ?? string.Empty,
                ["heldByPackageId"] = latest.PackageId ?? string.Empty
            },
            OccurredAt = decision.DecidedAt
        }, ct);

        var released = decision.Verdict == ApprovalVerdict.Approve;
        var outcome = new TransactionOutcome
        {
            TenantId = submission.TenantId,
            TransactionId = submission.TransactionId,
            EventName = "DualApproval",
            Outcome = released ? KernelOutcomes.Approved : KernelOutcomes.Rejected,
            Reason = $"Dual approval by '{decision.Approver}': {decision.Verdict}"
                + (string.IsNullOrWhiteSpace(decision.Comment) ? "" : $" — {decision.Comment}")
                + $" (hold placed by rule '{latest.RuleId}' of package '{latest.PackageId}' v{latest.PackageVersion}).",
            RuleId = latest.RuleId,
            PackageId = latest.PackageId,
            PackageVersion = latest.PackageVersion,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        await _outcomes.AppendAsync(outcome, ct);
        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = submission.TenantId,
            CorrelationId = submission.TransactionId,
            Category = AuditCategories.OutcomeRecorded,
            Actor = decision.Approver,
            Summary = $"Outcome '{outcome.Outcome}' recorded via dual approval: {outcome.Reason}",
            Detail = new Dictionary<string, string>
            {
                ["outcome"] = outcome.Outcome.ToString(),
                ["reason"] = outcome.Reason,
                ["eventName"] = outcome.EventName
            }
        }, ct);

        // Flywheel feedback re-entry: the flagging rule implicitly predicted
        // the record should not proceed. The human verdict is ground truth —
        // publish predicted-vs-actual as a ModelAccuracy score so the rule's
        // package accrues (or loses) earned trust over time.
        await _feedback.PublishModelAccuracyAsync(
            submission.TenantId,
            submission.TransactionId,
            KernelOutcomes.Rejected,
            outcome.Outcome,
            $"{latest.PackageId}@{latest.PackageVersion}:{latest.RuleId}",
            ct);

        // Notify packages that subscribed to the resolution, if declared.
        var followUp = outcome.Outcome;
        if (_registries.GetFor(submission.TenantId).Events.IsDeclared(followUp))
        {
            var payload = new Dictionary<string, string>(transaction.Payload, StringComparer.OrdinalIgnoreCase)
            {
                ["outcome"] = outcome.Outcome,
                ["reason"] = outcome.Reason,
                ["approver"] = decision.Approver
            };
            payload.TryAdd("transactionType", transaction.TransactionType);
            payload.TryAdd("submittedBy", transaction.SubmittedBy);

            await _bus.PublishAsync(new KernelEvent(
                submission.TenantId, followUp, submission.TransactionId, payload, DateTimeOffset.UtcNow), ct);
        }

        return ApprovalResult.Ok(outcome);
    }
}
