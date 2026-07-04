using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ECARMF.Kernel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddECARMFInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ECARMFDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }
}
