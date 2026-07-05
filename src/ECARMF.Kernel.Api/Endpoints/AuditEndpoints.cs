using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit")
            .RequirePermission(Permissions.AuditRead);

        group.MapGet("/transaction/{transactionId:guid}", async (
            Guid transactionId,
            HttpContext context,
            IAuditLog audit,
            IUserStore users,
            ITenantDirectory tenants,
            CancellationToken ct) =>
        {
            TenantResolution.TryGetTenant(context, out var tenantId);
            if (await SensitivityGuard.RequireAuditVisibilityAsync(context, users, tenants, tenantId, ct) is { } denied)
                return denied;
            var entries = await audit.GetByCorrelationAsync(tenantId, transactionId, ct);
            return Results.Ok(entries);
        });

        // A flywheel "cycle" is the audit trail of one correlation id:
        // received -> validated -> scored -> decided -> executed -> audited.
        group.MapGet("/cycle/{correlationId:guid}", async (
            Guid correlationId,
            HttpContext context,
            IAuditLog audit,
            IUserStore users,
            ITenantDirectory tenants,
            CancellationToken ct) =>
        {
            TenantResolution.TryGetTenant(context, out var tenantId);
            if (await SensitivityGuard.RequireAuditVisibilityAsync(context, users, tenants, tenantId, ct) is { } denied)
                return denied;
            var entries = await audit.GetByCorrelationAsync(tenantId, correlationId, ct);
            return Results.Ok(entries);
        });

        group.MapGet("/", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext context,
            IAuditLog audit,
            IUserStore users,
            ITenantDirectory tenants,
            CancellationToken ct) =>
        {
            TenantResolution.TryGetTenant(context, out var tenantId);
            if (await SensitivityGuard.RequireAuditVisibilityAsync(context, users, tenants, tenantId, ct) is { } denied)
                return denied;

            var rangeEnd = to ?? DateTimeOffset.UtcNow;
            var rangeStart = from ?? rangeEnd.AddHours(-24);

            if (rangeStart > rangeEnd)
            {
                return Results.BadRequest(new { error = "'from' must be earlier than 'to'." });
            }

            var entries = await audit.GetByTimeRangeAsync(tenantId, rangeStart, rangeEnd, ct);
            return Results.Ok(entries);
        });

        return app;
    }
}
