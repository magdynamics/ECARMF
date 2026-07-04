using System.Diagnostics.CodeAnalysis;

namespace ECARMF.Kernel.Application.Registries;

/// <summary>Common contract for kernel registries. An item name has exactly
/// one active owner at a time.</summary>
public interface IRegistry<TDeclaration>
{
    /// <summary>Registers a declaration under its unique name.</summary>
    /// <exception cref="RegistryConflictException">The name is already owned by an active package.</exception>
    void Register(TDeclaration declaration, string packageId, string packageVersion);

    /// <summary>Removes every item the given package version contributed.
    /// Called when a package is deactivated or a load is rolled back.</summary>
    void UnregisterPackage(string packageId, string packageVersion);

    bool TryGet(string name, [NotNullWhen(true)] out Registered<TDeclaration>? registration);

    bool Contains(string name);

    IReadOnlyList<Registered<TDeclaration>> GetAll();
}
