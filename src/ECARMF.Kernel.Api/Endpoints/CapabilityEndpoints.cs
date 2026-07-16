using ECARMF.Kernel.Application.Registries;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// The tenant's flattened capability index — one call instead of the
/// per-package manifest fan-out the Capability Explorer and ⌘K palette used
/// to make (40+ requests on a large tenant).
/// </summary>
public static class CapabilityEndpoints
{
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/capabilities")
            .RequirePermission(Domain.Identity.Permissions.RegistryRead);

        group.MapGet("/", async (
            HttpContext context, ICapabilityIndex index, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            return Results.Ok(await index.ForTenantAsync(tenantId, ct));
        });

        return app;
    }
}
