using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record SetAiSettingsRequest(string ApiKey, string? Model);

/// <summary>
/// Tenant-specific AI backend configuration. The platform serves multiple
/// clients: every tenant brings its own Anthropic API key, so AI usage,
/// billing, and behavior never cross tenants. Keys are write-only — status
/// reads return a masked hint, never the key.
/// </summary>
public static class AiSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAiSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/ai");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, ITenantAiSettingsStore settings, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            return Results.Ok(await settings.GetStatusAsync(tenantId, ct));
        });

        group.MapPut("/", async (
            SetAiSettingsRequest request, HttpContext context,
            IUserStore users, ITenantAiSettingsStore settings, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.ApiKey))
                return Results.BadRequest(new { error = "apiKey is required." });

            await settings.SetAsync(tenantId, request.ApiKey.Trim(), request.Model, user!.Identifier, ct);
            var status = await settings.GetStatusAsync(tenantId, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.AiSettingsUpdated,
                Actor = user.Identifier,
                Summary = $"Tenant AI backend configured (key {status.ApiKeyHint}, model {status.Model ?? "platform default"}).",
                Detail = new Dictionary<string, string>
                {
                    ["apiKeyHint"] = status.ApiKeyHint ?? string.Empty,
                    ["model"] = status.Model ?? string.Empty
                }
            }, ct);

            return Results.Ok(status);
        });

        group.MapDelete("/", async (
            HttpContext context, IUserStore users, ITenantAiSettingsStore settings, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await settings.ClearAsync(tenantId, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.AiSettingsUpdated,
                Actor = user!.Identifier,
                Summary = "Tenant AI backend credential removed; agents fall back to deterministic composers.",
                Detail = []
            }, ct);

            return Results.Ok(new TenantAiSettingsStatus(false, null, null, null, null));
        });

        return app;
    }
}
