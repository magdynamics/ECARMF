namespace ECARMF.Kernel.Domain.Packages;

/// <summary>Declares a domain entity type contributed by a Knowledge Package
/// (e.g. Venture). The kernel stores and validates instances of declared
/// entities without compile-time knowledge of them.</summary>
public class EntityDeclaration
{
    public string EntityTypeName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<AttributeDeclaration> Attributes { get; set; } = [];
}
