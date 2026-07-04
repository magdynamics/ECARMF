namespace ECARMF.Kernel.Domain.Transactions;

public enum ApprovalVerdict
{
    Approve,
    Reject
}

/// <summary>
/// A second approver's decision on a flagged transaction (the
/// RequireDualApproval control). Append-only: one decision per transaction,
/// never rewritten.
/// </summary>
public class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public Guid TransactionId { get; set; }

    public string Approver { get; set; } = string.Empty;

    public ApprovalVerdict Verdict { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
