using ECARMF.Kernel.Domain.Knowledge;

namespace ECARMF.Kernel.Application.Knowledge;

/// <summary>Tenant-scoped persistence for ad-hoc reference sources (links).</summary>
public interface IReferenceSourceStore
{
    Task<IReadOnlyList<ReferenceSource>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(ReferenceSource source, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default);
}
