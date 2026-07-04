namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for a transaction outcome. Insert-only.</summary>
public class OutcomeRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public Guid TransactionId { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? RuleId { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
