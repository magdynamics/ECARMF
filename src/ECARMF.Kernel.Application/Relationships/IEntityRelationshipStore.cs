using ECARMF.Kernel.Domain.Relationships;

namespace ECARMF.Kernel.Application.Relationships;

/// <summary>Tenant-scoped store for the generic entity-relationship graph
/// (Batch 3, Refinement 13). Reads are by subject so the engine can walk a
/// subject's outgoing edges — e.g. gather the child scores that roll up into
/// a CompositeHealth score (Refinement 14).</summary>
public interface IEntityRelationshipStore
{
    Task<EntityRelationship?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EntityRelationship>> GetAllAsync(string tenantId, CancellationToken ct = default);

    /// <summary>All outgoing edges from a subject, optionally narrowed to one
    /// relationshipType (e.g. only RollsUpInto edges for a rollup).</summary>
    Task<IReadOnlyList<EntityRelationship>> GetBySubjectAsync(
        string tenantId, string subjectType, string subjectId,
        string? relationshipType = null, CancellationToken ct = default);

    Task AddAsync(EntityRelationship relationship, CancellationToken ct = default);

    Task UpdateAsync(EntityRelationship relationship, CancellationToken ct = default);

    Task<bool> RemoveAsync(string tenantId, Guid id, CancellationToken ct = default);
}
