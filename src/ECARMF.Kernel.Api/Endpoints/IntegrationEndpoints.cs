using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Integrations;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Integrations;

namespace ECARMF.Kernel.Api.Endpoints;

public record CreateIntegrationRequest(
    string IntegrationId, string Name, string ApplicationType, string ConnectorId,
    string Mode, string? PullUrl, int? PullIntervalMinutes, string? AuthSecret);

public record IntegrationStatusRequest(string Status);

public record IntegrationFeedRequest(string RawPayload);

/// <summary>
/// Managed integrations with external applications (accounting, POS, billing,
/// real-estate management, ...). Configuration owns the relationship; the
/// referenced connector owns how payloads become records; every feed run is
/// history.
/// </summary>
public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IIntegrationStore integrations, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            return Results.Ok(await integrations.GetAllAsync(tenantId, ct));
        });

        group.MapGet("/application-types", () => Results.Ok(ApplicationTypes.All));

        group.MapPost("/", async (
            CreateIntegrationRequest request, HttpContext context,
            IUserStore users, IIntegrationStore integrations, IConnectorStore connectors,
            IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.IntegrationId) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "integrationId and name are required." });
            if (request.Mode is not ("push" or "pull"))
                return Results.BadRequest(new { error = "mode must be push or pull." });
            if (request.Mode == "pull" && string.IsNullOrWhiteSpace(request.PullUrl))
                return Results.BadRequest(new { error = "pull mode requires pullUrl." });
            if (await integrations.GetAsync(tenantId, request.IntegrationId, ct) is not null)
                return Results.BadRequest(new { error = $"Integration '{request.IntegrationId}' already exists." });

            await connectors.EnsureSeedConnectorsAsync(tenantId, ct);
            if (await connectors.GetAsync(tenantId, request.ConnectorId, ct) is null)
                return Results.BadRequest(new { error = $"Connector '{request.ConnectorId}' is not configured; create it first." });

            var integration = new IntegrationDefinition
            {
                TenantId = tenantId,
                IntegrationId = request.IntegrationId.Trim(),
                Name = request.Name.Trim(),
                ApplicationType = ApplicationTypes.All.Contains(request.ApplicationType) ? request.ApplicationType : "Custom",
                ConnectorId = request.ConnectorId,
                Mode = request.Mode,
                PullUrl = request.PullUrl,
                PullIntervalMinutes = request.PullIntervalMinutes,
                CreatedBy = user!.Identifier
            };
            await integrations.AddAsync(integration, ct);
            if (!string.IsNullOrWhiteSpace(request.AuthSecret))
            {
                await integrations.SetAuthSecretAsync(tenantId, integration.IntegrationId, request.AuthSecret, ct);
            }

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = integration.Id,
                Category = AuditCategories.IntegrationConfigured,
                Actor = user.Identifier,
                Summary = $"Integration '{integration.Name}' ({integration.ApplicationType}, {integration.Mode}) configured via connector '{integration.ConnectorId}'.",
                Detail = new Dictionary<string, string>
                {
                    ["integrationId"] = integration.IntegrationId,
                    ["applicationType"] = integration.ApplicationType,
                    ["connectorId"] = integration.ConnectorId,
                    ["mode"] = integration.Mode,
                    ["hasAuthSecret"] = (!string.IsNullOrWhiteSpace(request.AuthSecret)).ToString()
                }
            }, ct);

            return Results.Created($"/api/integrations/{integration.IntegrationId}", integration);
        });

        group.MapPost("/{integrationId}/status", async (
            string integrationId, IntegrationStatusRequest request, HttpContext context,
            IUserStore users, IIntegrationStore integrations, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (request.Status is not ("Active" or "Paused"))
                return Results.BadRequest(new { error = "status must be Active or Paused." });

            var integration = await integrations.GetAsync(tenantId, integrationId, ct);
            if (integration is null) return Results.NotFound();

            integration.Status = request.Status;
            await integrations.UpdateAsync(integration, ct);
            return Results.Ok(integration);
        });

        // Push: the external application delivers a feed.
        group.MapPost("/{integrationId}/feed", async (
            string integrationId, IntegrationFeedRequest request, HttpContext context,
            IUserStore users, IIntegrationFeedService feeds, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.RawPayload))
                return Results.BadRequest(new { error = "rawPayload is required." });

            var run = await feeds.PushAsync(tenantId, integrationId, request.RawPayload, user!.Identifier, ct);
            return run.Success ? Results.Ok(run) : Results.BadRequest(run);
        });

        // Pull: the platform fetches from the application's export endpoint.
        group.MapPost("/{integrationId}/pull", async (
            string integrationId, HttpContext context,
            IUserStore users, IIntegrationFeedService feeds, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            var run = await feeds.PullAsync(tenantId, integrationId, user!.Identifier, "pull-manual", ct);
            return run.Success ? Results.Ok(run) : Results.BadRequest(run);
        });

        group.MapGet("/runs", async (
            string? integrationId, int? limit, HttpContext context,
            IUserStore users, IIntegrationStore integrations, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            return Results.Ok(await integrations.GetRunsAsync(tenantId, integrationId, Math.Clamp(limit ?? 50, 1, 500), ct));
        });

        return app;
    }
}
