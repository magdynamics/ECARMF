namespace ECARMF.Kernel.Application.Registries;

/// <summary>
/// Thrown when a package attempts to register an item name that is already
/// owned by an active package. The kernel never silently overwrites a
/// registered control; the loader converts this into a failed load.
/// </summary>
public class RegistryConflictException : Exception
{
    public string ItemName { get; }
    public string OwningPackageId { get; }
    public string OwningPackageVersion { get; }

    public RegistryConflictException(string itemName, string owningPackageId, string owningPackageVersion)
        : base($"'{itemName}' is already registered by package '{owningPackageId}' version '{owningPackageVersion}'.")
    {
        ItemName = itemName;
        OwningPackageId = owningPackageId;
        OwningPackageVersion = owningPackageVersion;
    }
}
