using ECARMF.Kernel.Application.Registries;

namespace ECARMF.Kernel.Api.Endpoints;

public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/registries")
            .RequirePermission(Domain.Identity.Permissions.RegistryRead);

        group.MapGet("/entities", (HttpContext context, ITenantRegistryProvider registries) =>
            TenantResolution.TryGetTenant(context, out var tenantId)
                ? Results.Ok(registries.GetFor(tenantId).Entities.GetAll())
                : TenantResolution.MissingTenantResult());

        group.MapGet("/rules", (HttpContext context, ITenantRegistryProvider registries) =>
            TenantResolution.TryGetTenant(context, out var tenantId)
                ? Results.Ok(registries.GetFor(tenantId).Rules.GetAll())
                : TenantResolution.MissingTenantResult());

        group.MapGet("/events", (HttpContext context, ITenantRegistryProvider registries) =>
            TenantResolution.TryGetTenant(context, out var tenantId)
                ? Results.Ok(registries.GetFor(tenantId).Events.GetAll())
                : TenantResolution.MissingTenantResult());

        group.MapGet("/capabilities", (HttpContext context, ITenantRegistryProvider registries) =>
            TenantResolution.TryGetTenant(context, out var tenantId)
                ? Results.Ok(registries.GetFor(tenantId).Capabilities.GetAll())
                : TenantResolution.MissingTenantResult());

        return group;
    }
}
