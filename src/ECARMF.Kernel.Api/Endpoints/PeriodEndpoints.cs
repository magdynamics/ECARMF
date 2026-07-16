using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Period analysis: how a tenant is doing this period versus last, with
/// plain-language recommendations. Scoped to the calling tenant.
/// </summary>
public static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/analysis/periods", async (
            string? granularity, int? count, HttpContext context,
            IUserStore users, IPeriodAnalysisService analysis, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await analysis.AnalyzeAsync(tenantId, granularity ?? "month", count ?? 6, ct));
        });

        // Platform-wide risk overview (operator): risk across every tenant.
        app.MapGet("/api/platform/risk", async (
            HttpContext context, IUserStore users, IPlatformRiskService risk, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            return Results.Ok(await risk.OverviewAsync(ct));
        });

        // Operator action center: the ranked cross-tenant to-do.
        app.MapGet("/api/platform/actions", async (
            HttpContext context, IUserStore users, IPlatformActionService actions, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            return Results.Ok(await actions.ListAsync(ct));
        });

        return app;
    }
}
