using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Performance;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveOrgUnitRequest(
    string UnitId, string Name, string UnitType, string? ParentUnitId,
    string? Industry, string? Notes);

public record AttachUnitPackageRequest(string PackageId);

/// <summary>
/// The tenant's organizational shape: any hierarchy, any depth, all data.
/// Packages attach per unit; industry classification drives framework
/// suggestions through the same recommender the KPI layer uses.
/// </summary>
public static class OrgUnitEndpoints
{
    public static IEndpointRouteBuilder MapOrgUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/org-units");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IOrgUnitStore units, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            return Results.Ok(await units.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            SaveOrgUnitRequest request, HttpContext context,
            IUserStore users, IOrgUnitService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var (valid, message) = Validate(request);
            if (!valid) return Results.BadRequest(new { error = message });

            try
            {
                var unit = await service.CreateAsync(
                    tenantId, Slug(request.UnitId), request.Name.Trim(), request.UnitType.Trim(),
                    NullIfBlank(request.ParentUnitId), NullIfBlank(request.Industry),
                    request.Notes, user!.Identifier, ct);
                return Results.Created($"/api/org-units/{unit.UnitId}", unit);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{unitId}", async (
            string unitId, SaveOrgUnitRequest request, HttpContext context,
            IUserStore users, IOrgUnitService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var (valid, message) = Validate(request);
            if (!valid) return Results.BadRequest(new { error = message });

            try
            {
                return Results.Ok(await service.UpdateAsync(
                    tenantId, unitId, request.Name.Trim(), request.UnitType.Trim(),
                    NullIfBlank(request.ParentUnitId), NullIfBlank(request.Industry),
                    request.Notes, user!.Identifier, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapDelete("/{unitId}", async (
            string unitId, HttpContext context,
            IUserStore users, IOrgUnitService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            try
            {
                await service.DeleteAsync(tenantId, unitId, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{unitId}/packages", async (
            string unitId, AttachUnitPackageRequest request, HttpContext context,
            IUserStore users, IOrgUnitService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.PackageId))
                return Results.BadRequest(new { error = "packageId is required." });

            try
            {
                return Results.Ok(await service.AttachPackageAsync(
                    tenantId, unitId, request.PackageId.Trim(), user!.Identifier, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapDelete("/{unitId}/packages/{packageId}", async (
            string unitId, string packageId, HttpContext context,
            IUserStore users, IOrgUnitService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            try
            {
                return Results.Ok(await service.DetachPackageAsync(
                    tenantId, unitId, packageId, user!.Identifier, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // Industry-driven suggestions: the same recommender that powers the
        // KPI layer, applied to a unit's declared industry.
        group.MapGet("/{unitId}/suggestions", async (
            string unitId, HttpContext context, IUserStore users,
            IOrgUnitStore units, IFrameworkRecommender recommender, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            var unit = await units.GetAsync(tenantId, unitId, ct);
            if (unit is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(unit.Industry))
                return Results.Ok(new { unitId, industry = (string?)null, frameworks = Array.Empty<object>() });

            var frameworks = recommender.Recommend(tenantId, unit.Industry!)
                .Select(f => new
                {
                    frameworkId = f.Declaration.FrameworkId,
                    name = f.Declaration.Name,
                    packageId = f.PackageId,
                    packageVersion = f.PackageVersion,
                    alreadyAttached = unit.AttachedPackageIds.Contains(f.PackageId, StringComparer.OrdinalIgnoreCase)
                })
                .ToList();
            return Results.Ok(new { unitId, industry = unit.Industry, frameworks });
        });

        return app;
    }

    private static (bool Valid, string? Message) Validate(SaveOrgUnitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UnitId))
            return (false, "unitId is required (a short slug, e.g. orland-park-clinic).");
        if (string.IsNullOrWhiteSpace(request.Name))
            return (false, "name is required.");
        if (string.IsNullOrWhiteSpace(request.UnitType))
            return (false, "unitType is required (e.g. Division, Location, Property, Project).");
        return (true, null);
    }

    private static string Slug(string value) =>
        value.Trim().ToLowerInvariant().Replace(' ', '-');

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
