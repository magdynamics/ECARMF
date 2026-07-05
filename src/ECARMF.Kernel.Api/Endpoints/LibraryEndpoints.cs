using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>The tenant's source library: every upload archived verbatim and
/// searchable by metadata — with dozens of sources, the evidence behind any
/// record is always one query away.</summary>
public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        group.MapGet("/", async (
            string? query, string? sourceId, DateTimeOffset? from, DateTimeOffset? to, int? limit,
            HttpContext context, IUserStore users, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await library.SearchAsync(
                tenantId, query, sourceId, from, to, Math.Clamp(limit ?? 50, 1, 500), ct));
        });

        group.MapGet("/{id:guid}", async (
            Guid id, HttpContext context, IUserStore users, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var document = await library.GetAsync(tenantId, id, ct);
            return document is null ? Results.NotFound() : Results.Ok(document);
        });

        // The archived original, byte-for-byte.
        group.MapGet("/{id:guid}/content", async (
            Guid id, HttpContext context, IUserStore users, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var document = await library.GetAsync(tenantId, id, ct);
            var content = await library.GetContentAsync(tenantId, id, ct);
            if (document is null || content is null) return Results.NotFound();

            var contentType = document.MediaType switch
            {
                "pdf" => "application/pdf",
                "json" => "application/json",
                "csv" => "text/csv",
                _ => "text/plain"
            };
            return Results.File(content, contentType, document.FileName);
        });

        return app;
    }
}
