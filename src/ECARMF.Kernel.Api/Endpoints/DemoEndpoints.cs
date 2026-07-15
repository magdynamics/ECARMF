using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Onboarding;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Demo/training twins (operator console). Stand up "{tenant}-demo" with the
/// same skills and synthetic data so the platform can be demonstrated at full
/// capability without touching a client's real data.
/// </summary>
public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/platform/tenants/{tenantId}/demo", async (
            string tenantId, HttpContext context,
            IUserStore users, IDemoSeedingService demo, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            if (PlatformTenant.IsPlatform(tenantId))
                return Results.BadRequest(new { error = "The operator tenant has no demo twin." });

            try
            {
                return Results.Ok(await demo.SeedAsync(tenantId, op!.Identifier, ct));
            }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
