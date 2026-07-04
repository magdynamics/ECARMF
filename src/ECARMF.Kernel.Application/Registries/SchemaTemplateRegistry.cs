using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface ISchemaTemplateRegistry : IRegistry<SchemaTemplateDeclaration>;

/// <summary>Catalog of schema templates contributed by active Knowledge
/// Packages — the fifth kernel registry.</summary>
public class SchemaTemplateRegistry : RegistryBase<SchemaTemplateDeclaration>, ISchemaTemplateRegistry
{
    protected override string GetName(SchemaTemplateDeclaration declaration) => declaration.TemplateId;
}
