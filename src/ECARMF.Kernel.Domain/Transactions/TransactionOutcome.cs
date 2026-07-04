namespace ECARMF.Kernel.Domain.Transactions;

/// <summary>
/// The explainable result of processing an event for a transaction. Carries
/// the exact rule and package version that produced it (ECARMF-001 FND-0005);
/// rule fields are null only for the kernel's default outcome when no active
/// rule matched.
/// </summary>
public class TransactionOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public Guid TransactionId { get; set; }

    /// <summary>The event whose processing produced this outcome.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Package-defined outcome string (see KernelOutcomes for the
    /// kernel's well-known conventions).</summary>
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? RuleId { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
