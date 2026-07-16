using System.Reflection;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Infrastructure health probes for load balancers and monitoring — distinct
/// from the business "Health Board". These live outside /api so they are
/// never gated by the access-key requirement, and they carry no tenant data.
/// <list type="bullet">
///   <item><c>/health</c> — liveness: the process is up and serving.</item>
///   <item><c>/health/ready</c> — readiness: the database is reachable.</item>
/// </list>
/// </summary>
public static class HealthEndpoints
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: cheap, no dependencies. If this fails the process is down.
        // Health/auth-mode probes are exempt from rate limiting — a monitor
        // must never be throttled into a false "down".
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            version = Version,
            utc = DateTimeOffset.UtcNow
        })).DisableRateLimiting();

        // Auth mode: public (non-/api so the auth middleware never gates it) so
        // the UI can show a proper sign-in screen when the deployment is
        // key-only, instead of a dead header-identity bar.
        app.MapGet("/auth-mode", (IConfiguration config) => Results.Ok(new
        {
            headerIdentityAllowed = config.GetValue("Security:AllowHeaderIdentity", true)
        })).DisableRateLimiting();

        // Readiness: can we actually serve requests (is the DB reachable)?
        // A balancer should drain traffic when this returns 503.
        app.MapGet("/health/ready", async (ECARMFDbContext db, CancellationToken ct) =>
        {
            bool dbUp;
            try
            {
                dbUp = await db.Database.CanConnectAsync(ct);
            }
            catch
            {
                dbUp = false;
            }

            var body = new { status = dbUp ? "ready" : "not-ready", database = dbUp ? "up" : "down", utc = DateTimeOffset.UtcNow };
            return dbUp ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).DisableRateLimiting();

        return app;
    }
}
