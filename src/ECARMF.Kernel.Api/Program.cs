using ECARMF.Kernel.Api.Endpoints;
using ECARMF.Kernel.Api.Hosting;
using ECARMF.Kernel.Application;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Infrastructure;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ECARMF")
    ?? throw new InvalidOperationException("Connection string 'ECARMF' is not configured.");

builder.Services.AddECARMFApplication();
builder.Services.AddECARMFInfrastructure(connectionString);
builder.Services.AddHostedService<EventProcessingHostedService>();

var app = builder.Build();

app.MapTransactionEndpoints();

using (var scope = app.Services.CreateScope())
{
    if (app.Environment.IsDevelopment())
    {
        scope.ServiceProvider.GetRequiredService<ECARMFDbContext>().Database.Migrate();
    }

    // Registries are in-memory; rebuild them from persisted Active packages.
    await scope.ServiceProvider.GetRequiredService<IPackageLoader>().RehydrateActiveAsync();
}

app.Run();
