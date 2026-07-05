using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IAgentRegistry : IRegistry<AgentDeclaration>;

/// <summary>Catalog of specialized AI agents contributed by active Knowledge
/// Packages — the eighth kernel registry.</summary>
public class AgentRegistry : RegistryBase<AgentDeclaration>, IAgentRegistry
{
    protected override string GetName(AgentDeclaration declaration) => declaration.AgentId;
}
