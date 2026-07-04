using System.Text.Json;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Infrastructure.Persistence;

namespace ECARMF.Kernel.Api.Endpoints;

public static class DashboardEndpoints
{
    public record UpdateWidgetsRequest(JsonElement Widgets);

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IDashboardStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var dashboards = await store.GetAllAsync(tenantId, ct);
            return Results.Ok(dashboards.Select(d => new
            {
                d.Id,
                d.Name,
                Widgets = JsonSerializer.Deserialize<JsonElement>(d.WidgetsJson),
                d.UpdatedAt
            }));
        });

        // Live, editable config — no package rebuild involved.
        group.MapPut("/{id:guid}/widgets", async (
            Guid id, UpdateWidgetsRequest request, HttpContext context,
            IUserStore users, IDashboardStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            if (request.Widgets.ValueKind != JsonValueKind.Array)
                return Results.BadRequest(new { error = "widgets must be a JSON array." });

            var updated = await store.UpdateWidgetsAsync(tenantId, id, request.Widgets.GetRawText(), ct);
            return updated is null ? Results.NotFound() : Results.Ok(new { updated.Id, updated.UpdatedAt });
        });

        return app;
    }
}
