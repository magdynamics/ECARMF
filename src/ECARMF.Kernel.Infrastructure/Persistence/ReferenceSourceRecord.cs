using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ReferenceSourceRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public string? Jurisdiction { get; set; }
    public string Category { get; set; } = "ReferenceSource";
    public string? Description { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
}

public class EfReferenceSourceStore : IReferenceSourceStore
{
    private readonly ECARMFDbContext _db;

    public EfReferenceSourceStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReferenceSource>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.ReferenceSources.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.AddedAt)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(ReferenceSource source, CancellationToken ct = default)
    {
        _db.ReferenceSources.Add(new ReferenceSourceRecord
        {
            Id = source.Id,
            TenantId = source.TenantId,
            Title = source.Title,
            Url = source.Url,
            Issuer = source.Issuer,
            Jurisdiction = source.Jurisdiction,
            Category = source.Category,
            Description = source.Description,
            AddedBy = source.AddedBy,
            AddedAt = source.AddedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.ReferenceSources.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        if (record is not null)
        {
            _db.ReferenceSources.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static ReferenceSource ToDomain(ReferenceSourceRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        Title = r.Title,
        Url = r.Url,
        Issuer = r.Issuer,
        Jurisdiction = r.Jurisdiction,
        Category = r.Category,
        Description = r.Description,
        AddedBy = r.AddedBy,
        AddedAt = r.AddedAt
    };
}
