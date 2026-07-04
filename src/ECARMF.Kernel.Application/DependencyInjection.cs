using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace ECARMF.Kernel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddECARMFApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITenantRegistryProvider, TenantRegistryProvider>();
        services.AddSingleton<IKernelEventBus, InProcessKernelEventBus>();
        services.AddScoped<IPackageLoader, PackageLoader>();
        services.AddScoped<ITransactionIntakeService, TransactionIntakeService>();
        services.AddScoped<IEventProcessor, EventProcessor>();

        return services;
    }
}
