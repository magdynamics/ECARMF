namespace ECARMF.Kernel.Application.Operations;

public sealed record AuditRetentionResult(int Archived, DateTimeOffset Cutoff);

/// <summary>
/// Audit retention: MOVES audit entries older than the cutoff into the
/// archive table — never deletes them — so the live table stays fast while
/// the append-only history remains complete and examinable.
/// </summary>
public interface IAuditRetentionService
{
    Task<AuditRetentionResult> ArchiveAsync(int monthsToKeep, CancellationToken ct = default);
}
