using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// The knowledge base (Batch 2, Refinement 8 — supersedes /api/references):
/// effective-dated retrieval of knowledge assets contributed by active
/// packages. The asOf date is the whole point — asking a 2027 question
/// never returns 2025 rules, because every version carries an explicit
/// effective range validated at package load. Relationships expose the
/// knowledge graph; assetType filters by kind (ReferenceManual, SOP, ...).
/// </summary>
public static class KnowledgeAssetEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/knowledge-assets");

        group.MapGet("/", async (
            DateTimeOffset? asOf, string? docType, string? docKey, string? assetType, HttpContext context,
            IUserStore users, ITenantRegistryProvider registries, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            var effective = registries.GetFor(tenantId).KnowledgeAssets
                .GetEffective(asOf ?? DateTimeOffset.UtcNow, docType, docKey, assetType)
                .OrderBy(a => a.Declaration.DocKey, StringComparer.OrdinalIgnoreCase)
                .Select(a => new
                {
                    a.Declaration.AssetId,
                    a.Declaration.DocKey,
                    a.Declaration.Title,
                    a.Declaration.AssetType,
                    a.Declaration.DocType,
                    a.Declaration.Issuer,
                    a.Declaration.Jurisdiction,
                    a.Declaration.EffectiveFrom,
                    a.Declaration.EffectiveTo,
                    a.Declaration.Summary,
                    a.Declaration.ContentText,
                    a.Declaration.DocumentReference,
                    a.Declaration.Relationships,
                    a.Declaration.SemanticSearchEnabled,
                    a.PackageId,
                    a.PackageVersion
                });

            return Results.Ok(new { asOf = asOf ?? DateTimeOffset.UtcNow, assets = effective });
        });

        // The full catalog including superseded versions — the audit view of
        // what the knowledge base has ever contained, not what applies today.
        group.MapGet("/all", async (
            HttpContext context, IUserStore users, ITenantRegistryProvider registries, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            return Results.Ok(registries.GetFor(tenantId).KnowledgeAssets.GetAll()
                .OrderBy(a => a.Declaration.DocKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Declaration.EffectiveFrom)
                .Select(a => new
                {
                    a.Declaration.AssetId,
                    a.Declaration.DocKey,
                    a.Declaration.Title,
                    a.Declaration.AssetType,
                    a.Declaration.DocType,
                    a.Declaration.EffectiveFrom,
                    a.Declaration.EffectiveTo,
                    a.PackageId,
                    a.PackageVersion
                }));
        });

        return app;
    }
}
