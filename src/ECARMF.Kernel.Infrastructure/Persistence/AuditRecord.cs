namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for an audit entry. Insert-only.</summary>
public class AuditRecord
{
    public Guid Id { get; set; }

    public Guid CorrelationId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string DetailJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}
