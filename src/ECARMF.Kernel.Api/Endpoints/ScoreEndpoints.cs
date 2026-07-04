using ECARMF.Kernel.Application.Scoring;

namespace ECARMF.Kernel.Api.Endpoints;

public static class ScoreEndpoints
{
    public static IEndpointRouteBuilder MapScoreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scores")
            .RequirePermission(Domain.Identity.Permissions.ScoreRead);

        // Score history for one subject entity (trust, readiness, confidence,
        // control-effectiveness, treasury-efficiency — all ScoreRecords).
        group.MapGet("/{entityType}/{entityId}", async (
            string entityType,
            string entityId,
            HttpContext context,
            IScoreStore scores,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var history = await scores.GetHistoryAsync(tenantId, entityType, entityId, ct);
            return Results.Ok(history);
        });

        // Recent scores across the tenant, optionally filtered by score type.
        group.MapGet("/", async (
            int? limit,
            string? scoreType,
            HttpContext context,
            IScoreStore scores,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var take = Math.Clamp(limit ?? 100, 1, 500);
            return Results.Ok(await scores.GetRecentAsync(tenantId, take, scoreType, ct));
        });

        return app;
    }
}
