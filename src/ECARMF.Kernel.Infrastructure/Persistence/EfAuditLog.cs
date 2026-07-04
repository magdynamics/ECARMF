using System.Text.Json;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfAuditLog : IAuditLog
{
    private readonly ECARMFDbContext _db;

    public EfAuditLog(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        _db.AuditEntries.Add(ToRecord(entry));
        await _db.SaveChangesAsync(ct);
    }

    public async Task AppendManyAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        _db.AuditEntries.AddRange(entries.Select(ToRecord));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByCorrelationAsync(Guid correlationId, CancellationToken ct = default)
    {
        var records = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.CorrelationId == correlationId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);
        return records.Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByTimeRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.OccurredAt >= from && a.OccurredAt <= to)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);
        return records.Select(ToEntry).ToList();
    }

    private static AuditRecord ToRecord(AuditEntry entry) => new()
    {
        Id = entry.Id,
        CorrelationId = entry.CorrelationId,
        Category = entry.Category,
        Summary = entry.Summary,
        DetailJson = JsonSerializer.Serialize(entry.Detail),
        OccurredAt = entry.OccurredAt
    };

    private static AuditEntry ToEntry(AuditRecord record) => new()
    {
        Id = record.Id,
        CorrelationId = record.CorrelationId,
        Category = record.Category,
        Summary = record.Summary,
        Detail = JsonSerializer.Deserialize<Dictionary<string, string>>(record.DetailJson) ?? [],
        OccurredAt = record.OccurredAt
    };
}
