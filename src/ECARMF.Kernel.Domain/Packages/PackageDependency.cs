namespace ECARMF.Kernel.Domain.Packages;

/// <summary>A dependency on another Knowledge Package that must be active
/// before the declaring package can activate.</summary>
public class PackageDependency
{
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Lowest acceptable semantic version of the dependency.</summary>
    public string MinimumVersion { get; set; } = string.Empty;
}
