using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>A package as persisted for a tenant, with its lifecycle state.</summary>
public sealed record StoredPackage(
    string TenantId,
    KnowledgePackageManifest Manifest,
    PackageLoadState State,
    string? StatusDetail);

/// <summary>Persistence port for Knowledge Packages, implemented in
/// Infrastructure. All operations are tenant-scoped; only rehydration reads
/// across tenants.</summary>
public interface IPackageStore
{
    Task<bool> ExistsAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default);

    Task AddAsync(string tenantId, KnowledgePackageManifest manifest, PackageLoadState state, string? statusDetail, CancellationToken ct = default);

    Task<StoredPackage?> GetAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default);

    Task UpdateStateAsync(string tenantId, string packageId, string packageVersion, PackageLoadState state, string? statusDetail, CancellationToken ct = default);

    Task<IReadOnlyList<StoredPackage>> GetAllAsync(string tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<StoredPackage>> GetByStateAsync(string tenantId, PackageLoadState state, CancellationToken ct = default);

    /// <summary>All packages in the given state across every tenant.
    /// Used only by startup rehydration.</summary>
    Task<IReadOnlyList<StoredPackage>> GetByStateAllTenantsAsync(PackageLoadState state, CancellationToken ct = default);
}
