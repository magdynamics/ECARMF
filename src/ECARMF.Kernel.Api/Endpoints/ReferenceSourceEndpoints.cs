using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Knowledge;

namespace ECARMF.Kernel.Api.Endpoints;

public record AddReferenceSourceRequest(
    string Title, string Url, string? Issuer, string? Jurisdiction, string? Category, string? Description);

/// <summary>
/// Ad-hoc reference sources: a place to register an authoritative external link
/// (a state registry, a public-records portal, a regulator) as a knowledge
/// asset agents can cite — without authoring a package. This is the "paste a
/// link" door that packages' JSON manifests never offered.
/// </summary>
public static class ReferenceSourceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference-sources");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IReferenceSourceStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await store.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            AddReferenceSourceRequest request, HttpContext context,
            IUserStore users, IReferenceSourceStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "title is required." });
            if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return Results.BadRequest(new { error = "url must be a valid http(s) link." });

            var source = new ReferenceSource
            {
                TenantId = tenantId,
                Title = request.Title.Trim(),
                Url = uri.ToString(),
                Issuer = request.Issuer?.Trim(),
                Jurisdiction = request.Jurisdiction?.Trim(),
                Category = string.IsNullOrWhiteSpace(request.Category) ? "ReferenceSource" : request.Category.Trim(),
                Description = request.Description?.Trim(),
                AddedBy = user!.Identifier
            };
            await store.AddAsync(source, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = source.Id,
                Category = AuditCategories.IntegrationConfigured,
                Actor = user.Identifier,
                Summary = $"Reference source '{source.Title}' registered ({source.Url}).",
                Detail = new Dictionary<string, string>
                {
                    ["title"] = source.Title,
                    ["url"] = source.Url,
                    ["jurisdiction"] = source.Jurisdiction ?? "(neutral)",
                    ["category"] = source.Category
                }
            }, ct);

            return Results.Created($"/api/reference-sources/{source.Id}", source);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext context, IUserStore users, IReferenceSourceStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await store.DeleteAsync(tenantId, id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
