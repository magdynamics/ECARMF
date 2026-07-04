using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record AdvisorFeedbackRequest(bool Useful);

public static class AdvisorEndpoints
{
    public static IEndpointRouteBuilder MapAdvisorEndpoints(this IEndpointRouteBuilder app)
    {
        // Generate a brief. The requesting human is recorded; the brief itself
        // is produced and audited under the advisor's own AI-actor identity.
        app.MapPost("/api/advisor/briefs", async (
            HttpContext context, IUserStore users, IExecutiveAdvisor advisor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var brief = await advisor.GenerateBriefAsync(tenantId, user!.Identifier, ct);
            return Results.Created($"/api/advisor/briefs/{brief.Id}", brief);
        });

        app.MapGet("/api/advisor/briefs", async (
            int? limit, HttpContext context, IUserStore users, IAdvisorStore briefs, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await briefs.GetRecentAsync(tenantId, Math.Clamp(limit ?? 20, 1, 100), ct));
        });

        // Human verdict on a brief — feeds the advisor's ModelAccuracy trust
        // loop. The service refuses system actors; trust is earned from humans.
        app.MapPost("/api/advisor/briefs/{id:guid}/feedback", async (
            Guid id, AdvisorFeedbackRequest request, HttpContext context,
            IUserStore users, IExecutiveAdvisor advisor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var (success, message, brief) = await advisor.RecordFeedbackAsync(tenantId, id, request.Useful, user!, ct);
            return success ? Results.Ok(brief) : Results.BadRequest(new { error = message });
        });

        return app;
    }
}
