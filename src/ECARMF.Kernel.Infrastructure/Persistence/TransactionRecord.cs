namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for a received transaction. Insert-only:
/// nothing in the codebase updates or deletes rows in this table.</summary>
public class TransactionRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string TransactionType { get; set; } = string.Empty;

    public string SubmittedBy { get; set; } = string.Empty;

    /// <summary>Payload fields serialized as JSON.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Optional case/project the record is filed under.</summary>
    public string? CaseId { get; set; }
}
