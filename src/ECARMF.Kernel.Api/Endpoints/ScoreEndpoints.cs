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
        // riskOnly returns only risk-tagged scores (higher cap) so the risk
        // heatmap isn't crowded out by ordinary KPI volume.
        // ?unitRef= narrows to one organizational unit; tenant-wide scores
        // (UnitRef null) are included unless unitExclusive=true — a location's
        // posture includes what applies to the whole tenant.
        group.MapGet("/", async (
            int? limit,
            string? scoreType,
            bool? riskOnly,
            string? unitRef,
            bool? unitExclusive,
            HttpContext context,
            IScoreStore scores,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            IReadOnlyList<Domain.Scoring.ScoreRecord> result;
            if (riskOnly == true)
            {
                var riskTake = Math.Clamp(limit ?? 1000, 1, 5000);
                result = await scores.GetRecentRiskAsync(tenantId, riskTake, ct);
            }
            else
            {
                var take = Math.Clamp(limit ?? 100, 1, 500);
                result = await scores.GetRecentAsync(tenantId, take, scoreType, ct);
            }

            if (!string.IsNullOrWhiteSpace(unitRef))
            {
                result = result
                    .Where(s => string.Equals(s.UnitRef, unitRef, StringComparison.OrdinalIgnoreCase)
                                || (unitExclusive != true && s.UnitRef is null))
                    .ToList();
            }

            return Results.Ok(result);
        });

        return app;
    }
}
