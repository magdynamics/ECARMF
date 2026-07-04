namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persistence record for a ScoreRecord. Insert-only.</summary>
public class ScoreEntry
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string SubjectType { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;

    public string ScoreType { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public string? RuleId { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
