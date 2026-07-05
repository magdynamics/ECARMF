using ECARMF.Kernel.Application.Agents;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record AskAgentRequest(string Question);

public record AgentFeedbackRequest(bool Useful);

/// <summary>
/// Specialized AI agents declared by active packages: list them, consult
/// them, and rate their answers — the rating feeds each agent's own
/// ModelAccuracy trust history.
/// </summary>
public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IAgentConsultService agents, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(agents.ListAgents(tenantId).Select(a => new
            {
                a.Declaration.AgentId,
                a.Declaration.Name,
                a.Declaration.Description,
                a.Declaration.ContextSources,
                a.Declaration.SampleQuestions,
                a.PackageId,
                a.PackageVersion
            }));
        });

        group.MapPost("/{agentId}/ask", async (
            string agentId, AskAgentRequest request, HttpContext context,
            IUserStore users, IAgentConsultService agents, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var (success, message, interaction) = await agents.AskAsync(
                tenantId, agentId, request.Question, user!.Identifier, ct);
            return success ? Results.Ok(interaction) : Results.BadRequest(new { error = message });
        });

        group.MapGet("/interactions", async (
            string? agentId, int? limit, HttpContext context,
            IUserStore users, IAgentInteractionStore interactions, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await interactions.GetRecentAsync(tenantId, agentId, Math.Clamp(limit ?? 20, 1, 100), ct));
        });

        group.MapPost("/interactions/{id:guid}/feedback", async (
            Guid id, AgentFeedbackRequest request, HttpContext context,
            IUserStore users, IAgentConsultService agents, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var (success, message, interaction) = await agents.RecordFeedbackAsync(tenantId, id, request.Useful, user!, ct);
            return success ? Results.Ok(interaction) : Results.BadRequest(new { error = message });
        });

        return app;
    }
}
