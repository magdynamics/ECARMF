using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace ECARMF.Kernel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddECARMFApplication(this IServiceCollection services)
    {
        services.AddSingleton<IEntityRegistry, EntityRegistry>();
        services.AddSingleton<IRuleRegistry, RuleRegistry>();
        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();

        services.AddScoped<IPackageLoader, PackageLoader>();

        return services;
    }
}
