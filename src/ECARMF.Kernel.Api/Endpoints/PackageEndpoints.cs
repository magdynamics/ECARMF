using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Api.Endpoints;

public static class PackageEndpoints
{
    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        // Package lifecycle is Platform Administrator territory.
        var group = app.MapGroup("/api/packages")
            .RequirePermission(Domain.Identity.Permissions.PackageManage);

        // Upload (stage) a Knowledge Package manifest for the calling tenant.
        group.MapPost("/", async (
            KnowledgePackageManifest manifest,
            HttpContext context,
            IPackageLoader loader,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var result = await loader.LoadAsync(tenantId, manifest, ct);
            return result.Success
                ? Results.Created($"/api/packages/{manifest.PackageId}/{manifest.PackageVersion}", result)
                : Results.BadRequest(result);
        });

        // List the calling tenant's packages with their lifecycle state.
        group.MapGet("/", async (
            HttpContext context,
            IPackageStore store,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var packages = await store.GetAllAsync(tenantId, ct);

            // supersededBy is DERIVED (TCEL P2.1) — never stored on the older
            // manifest — by scanning every package's Supersedes list.
            var summaries = packages.Select(p => new
            {
                p.Manifest.PackageId,
                p.Manifest.Name,
                p.Manifest.PackageVersion,
                p.Manifest.Publisher,
                State = p.State.ToString(),
                p.StatusDetail,
                Entities = p.Manifest.Entities.Count,
                Rules = p.Manifest.Rules.Count,
                Events = p.Manifest.Events.Count,
                Capabilities = p.Manifest.Capabilities.Count,
                Supersedes = p.Manifest.Supersedes.Select(s => s.PackageId).ToList(),
                Consolidates = p.Manifest.Consolidates,
                SupersededBy = packages
                    .Where(other => other.Manifest.Supersedes.Any(s =>
                        string.Equals(s.PackageId, p.Manifest.PackageId, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(s.PackageVersion)
                            || string.Equals(s.PackageVersion, p.Manifest.PackageVersion, StringComparison.OrdinalIgnoreCase))))
                    .Select(other => $"{other.Manifest.PackageId}@{other.Manifest.PackageVersion}")
                    .ToList()
            });
            return Results.Ok(summaries);
        });

        // Inspect one package: full manifest + state.
        group.MapGet("/{packageId}/{version}", async (
            string packageId,
            string version,
            HttpContext context,
            IPackageStore store,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var package = await store.GetAsync(tenantId, packageId, version, ct);
            return package is null
                ? Results.NotFound(new { error = $"Package '{packageId}' version '{version}' is not loaded." })
                : Results.Ok(new
                {
                    State = package.State.ToString(),
                    package.StatusDetail,
                    package.Manifest
                });
        });

        group.MapPost("/{packageId}/{version}/activate", async (
            string packageId,
            string version,
            HttpContext context,
            IPackageLoader loader,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var result = await loader.ActivateAsync(tenantId, packageId, version, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapPost("/{packageId}/{version}/deactivate", async (
            string packageId,
            string version,
            HttpContext context,
            IPackageLoader loader,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var result = await loader.DeactivateAsync(tenantId, packageId, version, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // The ID ledger (TCEL P1.2): read-only projection of every registered
        // id per kind, with provenance. Scoped to Registry:Read — an authoring
        // pipeline polls it before generating the next wave and does not need
        // package-management rights to do so.
        app.MapGroup("/api/packages/id-ledger")
            .RequirePermission(Domain.Identity.Permissions.RegistryRead)
            .MapGet("/", async (
                HttpContext context,
                IPackageIdLedgerService ledger,
                CancellationToken ct) =>
            {
                if (!TenantResolution.TryGetTenant(context, out var tenantId))
                    return TenantResolution.MissingTenantResult();

                return Results.Ok(await ledger.BuildAsync(tenantId, ct));
            });

        return app;
    }
}
