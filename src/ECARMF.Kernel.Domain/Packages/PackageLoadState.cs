namespace ECARMF.Kernel.Domain.Packages;

/// <summary>Lifecycle state of a Knowledge Package in the kernel.</summary>
public enum PackageLoadState
{
    /// <summary>Validated and persisted, not yet contributing to the registries.</summary>
    Staged,

    /// <summary>Declarations registered; the package is live in the runtime.</summary>
    Active,

    /// <summary>Validation, dependency resolution, or registration failed. See StatusDetail.</summary>
    Failed,

    /// <summary>Previously active; its declarations have been withdrawn.</summary>
    Deactivated
}
