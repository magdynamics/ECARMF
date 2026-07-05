using System.Text.Json.Serialization;
using ECARMF.Kernel.Api.Endpoints;
using ECARMF.Kernel.Api.Hosting;
using ECARMF.Kernel.Application;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Infrastructure;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Lets the app run as a Windows service (no-op when run as a console).
builder.Host.UseWindowsService();

var connectionString = builder.Configuration.GetConnectionString("ECARMF")
    ?? throw new InvalidOperationException("Connection string 'ECARMF' is not configured.");

builder.Services.AddECARMFApplication();
builder.Services.AddECARMFInfrastructure(connectionString);
builder.Services.AddHostedService<EventProcessingHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.FeedSchedulerHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.RenewalMonitorHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.EmailDispatchHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.MonthlyReportHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.MonthlyBillingHostedService>();
builder.Services.AddHostedService<ECARMF.Kernel.Api.Hosting.TreasuryThresholdHostedService>();

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

// The API also serves the built admin UI (frontend build lands in wwwroot),
// so one address is the whole app — shareable across the network.
app.UseDefaultFiles();
app.UseStaticFiles();

// Credential-first authentication: an access key derives tenant + identity
// (headers are overwritten); suspended tenants are locked out platform-wide.
app.UseMiddleware<ECARMF.Kernel.Api.Hosting.ApiKeyAuthenticationMiddleware>();

app.MapRecordEndpoints();
app.MapAuditEndpoints();
app.MapPackageEndpoints();
app.MapRegistryEndpoints();
app.MapScoreEndpoints();
app.MapUserEndpoints();
app.MapConnectorEndpoints();
app.MapCapitalFlowEndpoints();
app.MapAnalyticsEndpoints();
app.MapDashboardEndpoints();
app.MapTaskEndpoints();
app.MapAdvisorEndpoints();
app.MapAiSettingsEndpoints();
app.MapPlatformEndpoints();
app.MapBillingEndpoints();
app.MapLibraryEndpoints();
app.MapIntegrationEndpoints();
app.MapBenchmarkEndpoints();
app.MapRenewalEndpoints();
app.MapMailEndpoints();
app.MapReportEndpoints();
app.MapBulkImportEndpoints();
app.MapTemplateEndpoints();
app.MapHealthBoardEndpoints();
app.MapHardeningEndpoints();
app.MapOrgUnitEndpoints();
app.MapTreasuryEndpoints();
app.MapAgentEndpoints();
app.MapReferenceEndpoints();
app.MapFundingEndpoints();

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
