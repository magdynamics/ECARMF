using ECARMF.Kernel.Application.Audit;

namespace ECARMF.Kernel.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit/transaction/{transactionId:guid}", async (
            Guid transactionId,
            HttpContext context,
            IAuditLog audit,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var entries = await audit.GetByCorrelationAsync(tenantId, transactionId, ct);
            return Results.Ok(entries);
        });

        // A flywheel "cycle" is the audit trail of one correlation id:
        // received -> validated -> scored -> decided -> executed -> audited.
        app.MapGet("/api/audit/cycle/{correlationId:guid}", async (
            Guid correlationId,
            HttpContext context,
            IAuditLog audit,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var entries = await audit.GetByCorrelationAsync(tenantId, correlationId, ct);
            return Results.Ok(entries);
        });

        app.MapGet("/api/audit", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext context,
            IAuditLog audit,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

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
