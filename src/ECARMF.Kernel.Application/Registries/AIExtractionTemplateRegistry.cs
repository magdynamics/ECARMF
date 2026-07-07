using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IAIExtractionTemplateRegistry : IRegistry<AIExtractionTemplateDeclaration>;

/// <summary>Catalog of AI extraction templates contributed by active
/// Knowledge Packages — the tenth kernel registry (AI Financial Analyst,
/// step 1).</summary>
public class AIExtractionTemplateRegistry : RegistryBase<AIExtractionTemplateDeclaration>, IAIExtractionTemplateRegistry
{
    protected override string GetName(AIExtractionTemplateDeclaration declaration) => declaration.TemplateId;
}
