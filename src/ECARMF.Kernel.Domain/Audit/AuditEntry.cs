namespace ECARMF.Kernel.Domain.Audit;

/// <summary>
/// One append-only audit fact. CorrelationId ties the entry to the
/// transaction (or package record) it concerns; Detail carries the
/// structured evidence for the entry.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public Dictionary<string, string> Detail { get; set; } = [];

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Well-known audit categories written by the kernel.</summary>
public static class AuditCategories
{
    public const string RecordReceived = "RecordReceived";
    public const string EventPublished = "EventPublished";
    public const string RuleEvaluated = "RuleEvaluated";
    public const string OutcomeRecorded = "OutcomeRecorded";
    public const string ApprovalRecorded = "ApprovalRecorded";
    public const string ScoreComputed = "ScoreComputed";
    public const string PackageLoaded = "PackageLoaded";
    public const string PackageActivated = "PackageActivated";
    public const string PackageDeactivated = "PackageDeactivated";
    public const string PackageFailed = "PackageFailed";
}
