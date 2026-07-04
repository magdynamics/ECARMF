namespace ECARMF.Kernel.Domain.Packages;

/// <summary>Declares a single attribute of a package-contributed entity or
/// event payload field.</summary>
public class AttributeDeclaration
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Logical data type: string, number, boolean, date, uuid.</summary>
    public string DataType { get; set; } = string.Empty;

    public bool Required { get; set; }

    public string? Description { get; set; }
}
