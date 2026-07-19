using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class DocumentAllocationRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? RecommendedUnitRef { get; set; }
    public string? RecommendedUnitName { get; set; }
    public string? DocumentType { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
    public string Status { get; set; } = DocumentAllocationStatuses.Pending;
    public string? DecidedUnitRef { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class EfDocumentAllocationStore : IDocumentAllocationStore
{
    private readonly ECARMFDbContext _db;

    public EfDocumentAllocationStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(DocumentAllocation a, CancellationToken ct = default)
    {
        _db.DocumentAllocations.Add(ToRecord(a));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DocumentAllocation?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var r = await _db.DocumentAllocations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task<IReadOnlyList<DocumentAllocation>> GetByStatusAsync(string tenantId, string? status, CancellationToken ct = default)
    {
        var q = _db.DocumentAllocations.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        var records = await q.OrderByDescending(x => x.CreatedAt).Take(2000).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task UpdateAsync(DocumentAllocation a, CancellationToken ct = default)
    {
        var r = await _db.DocumentAllocations.FirstAsync(x => x.TenantId == a.TenantId && x.Id == a.Id, ct);
        r.Status = a.Status;
        r.DecidedUnitRef = a.DecidedUnitRef;
        r.DecidedBy = a.DecidedBy;
        r.DecidedAt = a.DecidedAt;
        await _db.SaveChangesAsync(ct);
    }

    private static DocumentAllocationRecord ToRecord(DocumentAllocation a) => new()
    {
        Id = a.Id, TenantId = a.TenantId, DocumentId = a.DocumentId, FileName = a.FileName,
        RecommendedUnitRef = a.RecommendedUnitRef, RecommendedUnitName = a.RecommendedUnitName,
        DocumentType = a.DocumentType, Confidence = a.Confidence, Reasoning = a.Reasoning,
        Status = a.Status, DecidedUnitRef = a.DecidedUnitRef, DecidedBy = a.DecidedBy,
        CreatedAt = a.CreatedAt, DecidedAt = a.DecidedAt, CreatedBy = a.CreatedBy
    };

    private static DocumentAllocation ToDomain(DocumentAllocationRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, DocumentId = r.DocumentId, FileName = r.FileName,
        RecommendedUnitRef = r.RecommendedUnitRef, RecommendedUnitName = r.RecommendedUnitName,
        DocumentType = r.DocumentType, Confidence = r.Confidence, Reasoning = r.Reasoning,
        Status = r.Status, DecidedUnitRef = r.DecidedUnitRef, DecidedBy = r.DecidedBy,
        CreatedAt = r.CreatedAt, DecidedAt = r.DecidedAt, CreatedBy = r.CreatedBy
    };
}
