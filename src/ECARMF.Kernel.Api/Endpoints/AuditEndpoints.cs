using ECARMF.Kernel.Application.Operations;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record RetentionRequest(int? MonthsToKeep);

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

        // Retention (operator): MOVE entries older than monthsToKeep into the
        // archive table — never a delete; append-only history stays complete.
        app.MapPost("/api/platform/audit/retention", async (
            RetentionRequest request, HttpContext context,
            Application.Identity.IUserStore users, IAuditRetentionService retention,
            Application.Audit.IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;
            var months = request.MonthsToKeep ?? 24;
            if (months < 1) return Results.BadRequest(new { error = "monthsToKeep must be at least 1." });

            var result = await retention.ArchiveAsync(months, ct);

            await audit.AppendAsync(new Domain.Audit.AuditEntry
            {
                TenantId = Application.Identity.PlatformTenant.Id,
                CorrelationId = Guid.NewGuid(),
                Category = "AuditArchived",
                Actor = op!.Identifier,
                Summary = $"Audit retention run: {result.Archived} entr(ies) older than {result.Cutoff:yyyy-MM-dd} moved to the archive.",
                Detail = new Dictionary<string, string>
                {
                    ["archived"] = result.Archived.ToString(),
                    ["cutoff"] = result.Cutoff.ToString("O"),
                    ["monthsKept"] = months.ToString()
                }
            }, ct);

            return Results.Ok(result);
        });

        return app;
    }
}
