using ECARMF.Kernel.Application.Relationships;
using ECARMF.Kernel.Domain.Relationships;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EntityRelationshipRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string RelatedType { get; set; } = string.Empty;
    public string RelatedId { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public decimal? Strength { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfEntityRelationshipStore : IEntityRelationshipStore
{
    private readonly ECARMFDbContext _db;

    public EfEntityRelationshipStore(ECARMFDbContext db) => _db = db;

    public async Task<EntityRelationship?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.EntityRelationships.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<EntityRelationship>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.EntityRelationships.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.SubjectType).ThenBy(r => r.SubjectId).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<EntityRelationship>> GetBySubjectAsync(
        string tenantId, string subjectType, string subjectId,
        string? relationshipType = null, CancellationToken ct = default)
    {
        var query = _db.EntityRelationships.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                && r.SubjectType == subjectType && r.SubjectId == subjectId);
        if (!string.IsNullOrWhiteSpace(relationshipType))
        {
            query = query.Where(r => r.RelationshipType == relationshipType);
        }
        var records = await query.OrderBy(r => r.RelatedType).ThenBy(r => r.RelatedId).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(EntityRelationship relationship, CancellationToken ct = default)
    {
        _db.EntityRelationships.Add(new EntityRelationshipRecord
        {
            Id = relationship.Id, TenantId = relationship.TenantId,
            SubjectType = relationship.SubjectType, SubjectId = relationship.SubjectId,
            RelatedType = relationship.RelatedType, RelatedId = relationship.RelatedId,
            RelationshipType = relationship.RelationshipType, Strength = relationship.Strength,
            CreatedBy = relationship.CreatedBy, CreatedAt = relationship.CreatedAt,
            UpdatedAt = relationship.UpdatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(EntityRelationship relationship, CancellationToken ct = default)
    {
        var record = await _db.EntityRelationships.FirstAsync(
            r => r.TenantId == relationship.TenantId && r.Id == relationship.Id, ct);
        record.SubjectType = relationship.SubjectType;
        record.SubjectId = relationship.SubjectId;
        record.RelatedType = relationship.RelatedType;
        record.RelatedId = relationship.RelatedId;
        record.RelationshipType = relationship.RelationshipType;
        record.Strength = relationship.Strength;
        record.UpdatedAt = relationship.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.EntityRelationships
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        if (record is null) return false;
        _db.EntityRelationships.Remove(record);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static EntityRelationship ToDomain(EntityRelationshipRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId,
        SubjectType = r.SubjectType, SubjectId = r.SubjectId,
        RelatedType = r.RelatedType, RelatedId = r.RelatedId,
        RelationshipType = r.RelationshipType, Strength = r.Strength,
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}
