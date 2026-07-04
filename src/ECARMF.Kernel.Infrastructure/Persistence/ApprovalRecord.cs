namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for a dual-approval decision. Insert-only.</summary>
public class ApprovalRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public Guid TransactionId { get; set; }

    public string Approver { get; set; } = string.Empty;

    public string Verdict { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}
