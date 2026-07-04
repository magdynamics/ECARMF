using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // Forecast generation (AI actor or Owner).
        app.MapPost("/api/forecasts/generate", async (
            string subjectType, string subjectId, string scoreType,
            HttpContext context, IUserStore users, IForecastingEngine engine, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreWrite, ct);
            if (error is not null) return error;

            var forecast = await engine.ForecastNextAsync(tenantId, subjectType, subjectId, scoreType, ct);
            return forecast is null
                ? Results.Ok(new { message = "Not enough score history to forecast (need at least 2 points)." })
                : Results.Ok(forecast);
        });

        var deviations = app.MapGroup("/api/deviations");

        deviations.MapGet("/", async (
            int? limit, HttpContext context, IUserStore users, IDeviationStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;
            return Results.Ok(await store.GetRecentAsync(tenantId, Math.Clamp(limit ?? 50, 1, 200), ct));
        });

        deviations.MapPost("/{id:guid}/acknowledge", async (
            Guid id, HttpContext context, IUserStore users, IDeviationStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;

            var alert = await store.GetAsync(tenantId, id, ct);
            if (alert is null) return Results.NotFound();
            alert.AcknowledgedBy = user!.Identifier;
            alert.ResolvedAt = DateTimeOffset.UtcNow;
            await store.UpdateAsync(alert, ct);
            return Results.Ok(alert);
        });

        // Scheduled missing-data sweep: silence is a failure mode. Invoked by
        // an external scheduler (or manually) per tenant for MVP.
        deviations.MapPost("/check-missing", async (
            int? maxAgeHours, HttpContext context, IUserStore users, IDeviationMonitor monitor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreWrite, ct);
            if (error is not null) return error;

            var raised = await monitor.CheckMissingDataAsync(tenantId, TimeSpan.FromHours(maxAgeHours ?? 24), ct);
            return Results.Ok(new { alertsRaised = raised });
        });

        return app;
    }
}
