using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Relationships;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Relationships;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveRelationshipRequest(
    string SubjectType, string SubjectId, string RelatedType, string RelatedId,
    string? RelationshipType, decimal? Strength);

public record ComputeCompositeRequest(
    string SubjectType, string SubjectId, string? ChildScoreType, string? CompositeScoreType);

/// <summary>
/// Batch 3 endpoints: the generic entity-relationship graph (Refinement 13 —
/// a standalone edge usable by any subject type) and the CompositeHealth
/// rollup (Refinement 14 — a pattern that reads RollsUpInto edges and weights
/// child scores via the kernel's weighted-risk math, not a new mechanism).
/// </summary>
public static class Batch3Endpoints
{
    public static IEndpointRouteBuilder MapBatch3Endpoints(this IEndpointRouteBuilder app)
    {
        var edges = app.MapGroup("/api/entity-relationships");

        edges.MapGet("/", async (
            HttpContext context, IUserStore users, IEntityRelationshipStore store,
            string? subjectType, string? subjectId, string? relationshipType, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            if (!string.IsNullOrWhiteSpace(subjectType) && !string.IsNullOrWhiteSpace(subjectId))
                return Results.Ok(await store.GetBySubjectAsync(tenantId, subjectType, subjectId, relationshipType, ct));
            return Results.Ok(await store.GetAllAsync(tenantId, ct));
        });

        edges.MapPost("/", async (
            SaveRelationshipRequest request, HttpContext context,
            IUserStore users, IEntityRelationshipStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.SubjectType) || string.IsNullOrWhiteSpace(request.SubjectId)
                || string.IsNullOrWhiteSpace(request.RelatedType) || string.IsNullOrWhiteSpace(request.RelatedId))
                return Results.BadRequest(new { error = "subjectType, subjectId, relatedType, and relatedId are all required." });

            var relationship = new EntityRelationship
            {
                TenantId = tenantId,
                SubjectType = request.SubjectType.Trim(),
                SubjectId = request.SubjectId.Trim(),
                RelatedType = request.RelatedType.Trim(),
                RelatedId = request.RelatedId.Trim(),
                RelationshipType = string.IsNullOrWhiteSpace(request.RelationshipType)
                    ? RelationshipTypes.Correlates : request.RelationshipType.Trim(),
                Strength = request.Strength,
                CreatedBy = user!.Identifier
            };
            await store.AddAsync(relationship, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = relationship.Id,
                Category = AuditCategories.EntityRelationshipDefined,
                Actor = user.Identifier,
                Summary = $"Relationship {relationship.SubjectType}:{relationship.SubjectId} " +
                          $"--{relationship.RelationshipType}--> {relationship.RelatedType}:{relationship.RelatedId}" +
                          (relationship.Strength is { } s ? $" (strength {s})." : "."),
                Detail = new Dictionary<string, string>
                {
                    ["subjectType"] = relationship.SubjectType,
                    ["subjectId"] = relationship.SubjectId,
                    ["relatedType"] = relationship.RelatedType,
                    ["relatedId"] = relationship.RelatedId,
                    ["relationshipType"] = relationship.RelationshipType,
                    ["strength"] = relationship.Strength?.ToString() ?? ""
                }
            }, ct);

            return Results.Created($"/api/entity-relationships/{relationship.Id}", relationship);
        });

        edges.MapDelete("/{id:guid}", async (
            Guid id, HttpContext context,
            IUserStore users, IEntityRelationshipStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (!await store.RemoveAsync(tenantId, id, ct))
                return Results.NotFound();

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = id,
                Category = AuditCategories.EntityRelationshipRemoved,
                Actor = user!.Identifier,
                Summary = $"Relationship '{id}' removed by {user.Identifier}.",
                Detail = new Dictionary<string, string> { ["relationshipId"] = id.ToString() }
            }, ct);

            return Results.NoContent();
        });

        // CompositeHealth rollup (Refinement 14): compute a weighted rollup
        // score for a parent subject from its RollsUpInto child edges.
        app.MapPost("/api/composite-health/compute", async (
            ComputeCompositeRequest request, HttpContext context,
            IUserStore users, ICompositeHealthService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreWrite, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.SubjectType) || string.IsNullOrWhiteSpace(request.SubjectId))
                return Results.BadRequest(new { error = "subjectType and subjectId are required." });

            var score = await service.ComputeAsync(
                tenantId, request.SubjectType.Trim(), request.SubjectId.Trim(),
                string.IsNullOrWhiteSpace(request.ChildScoreType) ? null : request.ChildScoreType.Trim(),
                string.IsNullOrWhiteSpace(request.CompositeScoreType)
                    ? CompositeHealthService.DefaultScoreType : request.CompositeScoreType.Trim(),
                ct);

            return score is null
                ? Results.BadRequest(new { error = "No RollsUpInto edges with scored children were found for this subject." })
                : Results.Ok(score);
        });

        return app;
    }
}
