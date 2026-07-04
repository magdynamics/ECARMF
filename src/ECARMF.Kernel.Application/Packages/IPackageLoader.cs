using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

/// <summary>Governs the Knowledge Package lifecycle per tenant: stage,
/// activate, deactivate, and rebuild registries from persisted active
/// packages at startup.</summary>
public interface IPackageLoader
{
    /// <summary>Validates and stages a manifest for a tenant. Invalid manifests
    /// are persisted as Failed with the full error list, so the attempt is auditable.</summary>
    Task<PackageOperationResult> LoadAsync(string tenantId, KnowledgePackageManifest manifest, CancellationToken ct = default);

    /// <summary>Resolves dependencies and registers the package's declarations
    /// into the tenant's registries. Rolls back and marks Failed on conflict.</summary>
    Task<PackageOperationResult> ActivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default);

    /// <summary>Withdraws the package's declarations from the tenant's
    /// registries. Refused while another active package of the same tenant
    /// depends on it.</summary>
    Task<PackageOperationResult> DeactivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default);

    /// <summary>Re-registers all Active packages of every tenant into their
    /// tenant's in-memory registries, in dependency order. Called once at startup.</summary>
    Task RehydrateActiveAsync(CancellationToken ct = default);
}
