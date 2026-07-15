using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Skills on a tenant's profile (operator console). A skill is a knowledge
/// package presented commercially — a tier and a price — that the operator can
/// turn on or off for a client. Activating installs it from the library (with
/// dependencies); priced skills flow through to billing. Operator-only, target
/// tenant in the path — the same shape as the rest of the platform surface.
/// </summary>
public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/tenants/{tenantId}/skills");

        group.MapGet("/", async (
            string tenantId, HttpContext context,
            IUserStore users, ITenantDirectory tenants, ISkillCatalog skills, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            if (await tenants.GetAsync(tenantId, ct) is null)
                return Results.NotFound(new { error = $"Tenant '{tenantId}' is not onboarded." });

            return Results.Ok(await skills.ListForTenantAsync(tenantId, ct));
        });

        group.MapPost("/{packageId}/activate", async (
            string tenantId, string packageId, HttpContext context,
            IUserStore users, ITenantDirectory tenants, ISkillCatalog skills, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            if (PlatformTenant.IsPlatform(tenantId))
                return Results.BadRequest(new { error = "The operator tenant holds no skills — pick a client." });
            if (await tenants.GetAsync(tenantId, ct) is null)
                return Results.NotFound(new { error = $"Tenant '{tenantId}' is not onboarded." });

            var result = await skills.ActivateAsync(packageId, tenantId, op!.Identifier, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapPost("/{packageId}/deactivate", async (
            string tenantId, string packageId, HttpContext context,
            IUserStore users, ITenantDirectory tenants, ISkillCatalog skills, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            if (await tenants.GetAsync(tenantId, ct) is null)
                return Results.NotFound(new { error = $"Tenant '{tenantId}' is not onboarded." });

            var result = await skills.DeactivateAsync(packageId, tenantId, op!.Identifier, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return app;
    }
}
