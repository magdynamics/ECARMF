using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>Governs the Knowledge Package lifecycle: stage, activate, deactivate,
/// and rebuild registries from persisted active packages at startup.</summary>
public interface IPackageLoader
{
    /// <summary>Validates and stages a manifest. Invalid manifests are persisted
    /// as Failed with the full error list, so the attempt is auditable.</summary>
    Task<PackageOperationResult> LoadAsync(KnowledgePackageManifest manifest, CancellationToken ct = default);

    /// <summary>Resolves dependencies and registers the package's declarations.
    /// Rolls back registrations and marks the package Failed on conflict.</summary>
    Task<PackageOperationResult> ActivateAsync(string packageId, string packageVersion, CancellationToken ct = default);

    /// <summary>Withdraws the package's declarations. Refused while another
    /// active package depends on it.</summary>
    Task<PackageOperationResult> DeactivateAsync(string packageId, string packageVersion, CancellationToken ct = default);

    /// <summary>Re-registers all Active packages into the in-memory registries,
    /// in dependency order. Called once at startup.</summary>
    Task RehydrateActiveAsync(CancellationToken ct = default);
}
