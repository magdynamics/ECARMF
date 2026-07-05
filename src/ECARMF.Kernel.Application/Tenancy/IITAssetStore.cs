using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Application.Tenancy;

/// <summary>Tenant-scoped IT asset inventory (Batch 2, Refinement 9).</summary>
public interface IITAssetStore
{
    Task<ITAsset?> GetAsync(string tenantId, string assetId, CancellationToken ct = default);
    Task<IReadOnlyList<ITAsset>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(ITAsset asset, CancellationToken ct = default);
    Task UpdateAsync(ITAsset asset, CancellationToken ct = default);
}
