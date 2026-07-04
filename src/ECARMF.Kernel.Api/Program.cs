using System.Text.Json.Serialization;
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

// Manifests declare operators and outcomes by name (e.g. "GreaterThan").
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "ECARMF Platform Kernel API",
        Version = "v1",
        Description = "Knowledge-driven runtime: packages contribute entities, events, rules, and capabilities; the kernel executes them as metadata."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapRecordEndpoints();
app.MapAuditEndpoints();
app.MapPackageEndpoints();
app.MapRegistryEndpoints();
app.MapScoreEndpoints();

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
