using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Registries;

public interface IPerformanceFrameworkRegistry : IRegistry<PerformanceFrameworkDeclaration>;

/// <summary>Catalog of performance (KPI/OKR) frameworks contributed by active
/// Knowledge Packages — the sixth kernel registry.</summary>
public class PerformanceFrameworkRegistry : RegistryBase<PerformanceFrameworkDeclaration>, IPerformanceFrameworkRegistry
{
    protected override string GetName(PerformanceFrameworkDeclaration declaration) => declaration.FrameworkId;
}
