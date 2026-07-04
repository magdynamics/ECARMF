namespace ECARMF.Kernel.Application.Registries;

/// <summary>
/// A declaration registered in a kernel registry, wrapped with the provenance
/// of the Knowledge Package version that contributed it. Provenance is what
/// lets every outcome cite the exact rule and package version that produced it
/// (ECARMF-001 FND-0005).
/// </summary>
public sealed record Registered<TDeclaration>(
    TDeclaration Declaration,
    string PackageId,
    string PackageVersion,
    DateTimeOffset RegisteredAt);
