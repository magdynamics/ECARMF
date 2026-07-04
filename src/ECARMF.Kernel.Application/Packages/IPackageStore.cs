using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>A package as persisted, with its lifecycle state.</summary>
public sealed record StoredPackage(
    KnowledgePackageManifest Manifest,
    PackageLoadState State,
    string? StatusDetail);

/// <summary>Persistence port for Knowledge Packages, implemented in Infrastructure.</summary>
public interface IPackageStore
{
    Task<bool> ExistsAsync(string packageId, string packageVersion, CancellationToken ct = default);

    Task AddAsync(KnowledgePackageManifest manifest, PackageLoadState state, string? statusDetail, CancellationToken ct = default);

    Task<StoredPackage?> GetAsync(string packageId, string packageVersion, CancellationToken ct = default);

    Task UpdateStateAsync(string packageId, string packageVersion, PackageLoadState state, string? statusDetail, CancellationToken ct = default);

    Task<IReadOnlyList<StoredPackage>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StoredPackage>> GetByStateAsync(PackageLoadState state, CancellationToken ct = default);
}
