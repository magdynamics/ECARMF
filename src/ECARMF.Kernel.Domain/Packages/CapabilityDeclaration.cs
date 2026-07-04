namespace ECARMF.Kernel.Domain.Packages;

/// <summary>Declares a named runtime capability a package contributes
/// (e.g. RequireDualApproval). Capabilities are discoverable through the
/// CapabilityRegistry and referenced by rules and downstream packages.</summary>
public class CapabilityDeclaration
{
    public string CapabilityId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
