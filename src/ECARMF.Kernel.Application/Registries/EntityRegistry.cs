using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IEntityRegistry : IRegistry<EntityDeclaration>;

/// <summary>Catalog of entity types contributed by active Knowledge Packages.</summary>
public class EntityRegistry : RegistryBase<EntityDeclaration>, IEntityRegistry
{
    protected override string GetName(EntityDeclaration declaration) => declaration.EntityTypeName;
}
