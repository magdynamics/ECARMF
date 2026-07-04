using ECARMF.Kernel.Domain.Audit;

namespace ECARMF.Kernel.Application.Audit;

/// <summary>
/// Append-only audit port. No update or delete members exist by design.
/// Callers must await appends before any downstream processing depends on
/// the audited fact (audit integrity first). Queries are tenant-scoped: one
/// client can never read another client's audit trail.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);

    Task AppendManyAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default);

    Task<IReadOnlyList<AuditEntry>> GetByCorrelationAsync(string tenantId, Guid correlationId, CancellationToken ct = default);

    Task<IReadOnlyList<AuditEntry>> GetByTimeRangeAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
