using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

public record InstallCatalogPackageRequest(string PackageId, string Version, bool? WithDependencies);

/// <summary>
/// The platform package library (operator console). Every package loaded into
/// any tenant is an installable unit; this exposes the union as a catalog and
/// installs a chosen package (with its dependencies) into a target tenant —
/// turning tenant-authored capabilities into platform-level offerings.
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IPackageCatalog catalog, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            return Results.Ok(await catalog.ListAsync(ct));
        });

        group.MapGet("/{packageId}/{version}", async (
            string packageId, string version, HttpContext context,
            IUserStore users, IPackageCatalog catalog, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var manifest = await catalog.GetManifestAsync(packageId, version, ct);
            return manifest is null
                ? Results.NotFound(new { error = $"Package '{packageId}' version '{version}' is not in the catalog." })
                : Results.Ok(manifest);
        });

        // Install a catalog package into a target tenant. Lives under the
        // platform-tenants group so it reads as an operator action on a client.
        app.MapPost("/api/platform/tenants/{tenantId}/catalog/install", async (
            string tenantId, InstallCatalogPackageRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IPackageCatalog catalog, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.PackageId) || string.IsNullOrWhiteSpace(request.Version))
                return Results.BadRequest(new { error = "packageId and version are required." });
            if (PlatformTenant.IsPlatform(tenantId))
                return Results.BadRequest(new { error = "The operator tenant holds no business data — install into a client tenant." });
            if (await tenants.GetAsync(tenantId, ct) is null)
                return Results.NotFound(new { error = $"Tenant '{tenantId}' is not onboarded." });

            var result = await catalog.InstallAsync(
                request.PackageId, request.Version, tenantId, op!.Identifier,
                request.WithDependencies ?? true, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
