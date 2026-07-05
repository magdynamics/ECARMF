using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Operations;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>The operator's portfolio board: every client's open alarms,
/// work, approaching lapses, feed health, and volume — worst first.</summary>
public static class HealthBoardEndpoints
{
    public static IEndpointRouteBuilder MapHealthBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/platform/health", async (
            HttpContext context, IUserStore users, IPlatformHealthService health, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            return Results.Ok(await health.GetHealthAsync(ct));
        });

        return app;
    }
}
