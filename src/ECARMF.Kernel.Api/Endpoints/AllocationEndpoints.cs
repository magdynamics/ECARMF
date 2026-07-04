using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public static class AllocationEndpoints
{
    public record DecisionRequest(string Action, decimal? ModifiedAmount, string? Comment);

    public static IEndpointRouteBuilder MapAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/allocations");

        // The AI actor (and the Owner) can generate recommendations.
        group.MapPost("/generate", async (
            HttpContext context, IUserStore users, ICapitalAllocationEngine engine, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreWrite, ct);
            if (error is not null) return error;

            var recommendation = await engine.GenerateAsync(tenantId, user!.Identifier, ct);
            return recommendation is null
                ? Results.Ok(new { message = "No AssetReadiness scores exist yet; nothing to recommend." })
                : Results.Ok(recommendation);
        });

        group.MapGet("/", async (
            int? limit, HttpContext context, IUserStore users, IAllocationStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await store.GetRecentAsync(tenantId, Math.Clamp(limit ?? 20, 1, 100), ct));
        });

        // Human decision on a recommendation — the same capability-scoped
        // permission as dual approval; the engine additionally refuses
        // system actors so an AI can never self-approve an escalation.
        group.MapPost("/{id:guid}/decision", async (
            Guid id, DecisionRequest request, HttpContext context,
            IUserStore users, ICapitalAllocationEngine engine, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;

            var (success, failure, recommendation) = await engine.DecideAsync(
                tenantId, id, user!, new AllocationDecision(request.Action, request.ModifiedAmount, request.Comment), ct);

            return success ? Results.Ok(recommendation) : Results.BadRequest(new { error = failure });
        });

        return app;
    }
}
