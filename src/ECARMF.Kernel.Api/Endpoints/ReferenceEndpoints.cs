using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// The Knowledge Reference Library (MagCPA Requirement 5): effective-dated
/// retrieval of regulatory/professional references contributed by active
/// packages. The asOf date is the whole point — asking a 2027 question
/// never returns 2025 rules, because every version carries an explicit
/// effective range validated at package load.
/// </summary>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/references");

        group.MapGet("/", async (
            DateTimeOffset? asOf, string? docType, string? docKey, HttpContext context,
            IUserStore users, ITenantRegistryProvider registries, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            var effective = registries.GetFor(tenantId).References
                .GetEffective(asOf ?? DateTimeOffset.UtcNow, docType, docKey)
                .OrderBy(r => r.Declaration.DocKey, StringComparer.OrdinalIgnoreCase)
                .Select(r => new
                {
                    r.Declaration.ReferenceId,
                    r.Declaration.DocKey,
                    r.Declaration.Title,
                    r.Declaration.DocType,
                    r.Declaration.Issuer,
                    r.Declaration.Jurisdiction,
                    r.Declaration.EffectiveFrom,
                    r.Declaration.EffectiveTo,
                    r.Declaration.Summary,
                    r.Declaration.ContentText,
                    r.PackageId,
                    r.PackageVersion
                });

            return Results.Ok(new { asOf = asOf ?? DateTimeOffset.UtcNow, references = effective });
        });

        // The full catalog including superseded versions — the audit view of
        // what the library has ever contained, not what applies today.
        group.MapGet("/all", async (
            HttpContext context, IUserStore users, ITenantRegistryProvider registries, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            return Results.Ok(registries.GetFor(tenantId).References.GetAll()
                .OrderBy(r => r.Declaration.DocKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Declaration.EffectiveFrom)
                .Select(r => new
                {
                    r.Declaration.ReferenceId,
                    r.Declaration.DocKey,
                    r.Declaration.Title,
                    r.Declaration.DocType,
                    r.Declaration.EffectiveFrom,
                    r.Declaration.EffectiveTo,
                    r.PackageId,
                    r.PackageVersion
                }));
        });

        return app;
    }
}
