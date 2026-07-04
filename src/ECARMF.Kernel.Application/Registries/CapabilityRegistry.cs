using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface ICapabilityRegistry : IRegistry<CapabilityDeclaration>;

/// <summary>Catalog of runtime capabilities contributed by active Knowledge Packages.</summary>
public class CapabilityRegistry : RegistryBase<CapabilityDeclaration>, ICapabilityRegistry
{
    protected override string GetName(CapabilityDeclaration declaration) => declaration.CapabilityId;
}
