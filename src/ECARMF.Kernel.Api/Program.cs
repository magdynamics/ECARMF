using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ECARMF.Kernel.Api.Endpoints;
using ECARMF.Kernel.Api.Hosting;
using ECARMF.Kernel.Application;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Infrastructure;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// HTTPS is enabled purely by configuration: define Kestrel:Endpoints (Http +
// Https with a certificate) in appsettings.Production.json — Kestrel binds
// them natively, no code path needed. NOTE: when Kestrel:Endpoints exists it
// REPLACES --urls, so the config must declare BOTH endpoints (see
// deploy/RUNBOOK-golive-and-ai.md, "Enable HTTPS"). With no Kestrel section,
// behavior is unchanged (--urls only).

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

// Abuse ceiling + brute-force protection. The global limiter is per-client-IP
// and generous (UI screens burst ~40 calls); the "auth-sensitive" policy
// guards credential issuance/rotation and AI-key configuration. It is a
// single shared window (not per-IP) — acceptable for a single-box deployment
// where those routes are rare, deliberate operator actions.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0
            }));
    options.AddFixedWindowLimiter("auth-sensitive", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

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
else
{
    // Only emits the Strict-Transport-Security header on HTTPS responses, so
    // this is inert until an HTTPS endpoint is configured.
    app.UseHsts();
}

// The API also serves the built admin UI (frontend build lands in wwwroot),
// so one address is the whole app — shareable across the network.
// index.html must never be cached: it names the current hashed bundle, and
// a cached copy pins users to a stale app after every deploy ("it's still
// broken" reports that a hard refresh fixes). The hashed assets themselves
// are immutable and can cache forever.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var isHashedAsset = ctx.File.Name.Contains('-') && ctx.Context.Request.Path.StartsWithSegments("/assets");
        ctx.Context.Response.Headers.CacheControl = isHashedAsset
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});

// Infrastructure liveness/readiness probes, mapped BEFORE auth so a load
// balancer or monitor can reach them without a key. They expose no tenant data.
app.MapHealthEndpoints();

// Rate limiting sits BEFORE authentication so brute-force attempts are
// throttled without paying the key-hash lookup. Health probes are exempt
// (the health group is marked DisableRateLimiting).
app.UseRateLimiter();

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
app.MapCatalogEndpoints();
app.MapSkillEndpoints();
app.MapDemoEndpoints();
app.MapPeriodEndpoints();
app.MapCaseEndpoints();
app.MapCapabilityEndpoints();
app.MapRiskTreatmentEndpoints();
app.MapReferenceSourceEndpoints();
app.MapDocumentTriageEndpoints();
app.MapDocumentIntelligenceEndpoints();
app.MapHealthBoardEndpoints();
app.MapHardeningEndpoints();
app.MapOrgUnitEndpoints();
app.MapTreasuryEndpoints();
app.MapAgentEndpoints();
app.MapKnowledgeAssetEndpoints();
app.MapFundingEndpoints();
app.MapBatch2Endpoints();
app.MapBatch3Endpoints();
app.MapPhiEndpoints();
app.MapFinancialStatementEndpoints();

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
