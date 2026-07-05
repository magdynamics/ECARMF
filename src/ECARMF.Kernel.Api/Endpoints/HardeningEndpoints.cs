using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record SetUserRolesRequest(string[] Roles);

public record SetSensitivityRequest(string Tier);

/// <summary>
/// Platform hardening batch: month-close billing for the whole portfolio,
/// regulator-ready audit export, per-user role management, and anonymized
/// peer benchmarking.
/// </summary>
public static class HardeningEndpoints
{
    public static IEndpointRouteBuilder MapHardeningEndpoints(this IEndpointRouteBuilder app)
    {
        // Operator: close the previous month for every active client now
        // (the scheduler does the same automatically).
        app.MapPost("/api/platform/billing/run", async (
            HttpContext context, IUserStore users, IMonthlyBillingService billing, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var generated = await billing.EnsureMonthlyStatementsAsync(DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { generated });
        });

        // The audit trail as CSV — for an examiner, verbatim.
        app.MapGet("/api/audit/export", async (
            DateTimeOffset? from, DateTimeOffset? to, string? category,
            HttpContext context, IUserStore users, ITenantDirectory tenants,
            IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.AuditRead, ct);
            if (error is not null) return error;
            if (await SensitivityGuard.RequireAuditVisibilityAsync(context, users, tenants, tenantId, ct) is { } denied)
                return denied;

            var start = from ?? DateTimeOffset.UtcNow.AddMonths(-12);
            var end = to ?? DateTimeOffset.UtcNow;
            var entries = await audit.GetByTimeRangeAsync(tenantId, start, end, ct);
            if (!string.IsNullOrWhiteSpace(category))
            {
                entries = entries.Where(e =>
                    string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var csv = AuditCsvExporter.ToCsv(entries);
            return Results.File(csv, "text/csv",
                $"audit-{tenantId}-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
        });

        // Your number against the anonymized peer distribution.
        app.MapGet("/api/analytics/peer-benchmark", async (
            string scoreType, HttpContext context,
            IUserStore users, IPeerBenchmarkService peers, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(scoreType))
                return Results.BadRequest(new { error = "scoreType is required." });

            return Results.Ok(await peers.CompareAsync(tenantId, scoreType.Trim(), ct));
        });

        // Operator: set a client's sensitivity tier; protections apply
        // automatically from the next request onward.
        app.MapPut("/api/platform/tenants/{tenantId}/sensitivity", async (
            string tenantId, SetSensitivityRequest request,
            HttpContext context, IUserStore users, ITenantDirectory tenants,
            IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (!Domain.Tenancy.SensitivityTiers.Ordered.Contains(request.Tier, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new
                {
                    error = "tier must be one of: " + string.Join(", ", Domain.Tenancy.SensitivityTiers.Ordered)
                });

            var profile = await tenants.GetAsync(tenantId, ct);
            if (profile is null) return Results.NotFound();

            var previous = profile.SensitivityTier;
            profile.SensitivityTier = Domain.Tenancy.SensitivityTiers.Ordered.First(t =>
                string.Equals(t, request.Tier, StringComparison.OrdinalIgnoreCase));
            await tenants.UpdateAsync(profile, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.TenantSensitivityChanged,
                Actor = op!.Identifier,
                Summary = $"Sensitivity tier changed from {previous} to {profile.SensitivityTier}.",
                Detail = new Dictionary<string, string> { ["previous"] = previous, ["current"] = profile.SensitivityTier }
            }, ct);

            return Results.Ok(new { tenantId, sensitivityTier = profile.SensitivityTier });
        });

        // Operator: change what a client user may do.
        app.MapPut("/api/platform/tenants/{tenantId}/users/{identifier}/roles", async (
            string tenantId, string identifier, SetUserRolesRequest request,
            HttpContext context, IUserStore users, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (request.Roles is not { Length: > 0 })
                return Results.BadRequest(new { error = "roles must contain at least one role." });
            var invalid = request.Roles.Where(r => !RoleCatalog.RolePermissions.ContainsKey(r)).ToList();
            if (invalid.Count > 0)
                return Results.BadRequest(new
                {
                    error = $"Unknown role(s): {string.Join(", ", invalid)}. " +
                            $"Valid roles: {string.Join(", ", RoleCatalog.RolePermissions.Keys)}."
                });

            var user = await users.GetByIdentifierAsync(tenantId, identifier, ct);
            if (user is null) return Results.NotFound();
            if (user.IsSystemActor)
                return Results.BadRequest(new { error = "System actor roles are fixed by the kernel." });

            var before = string.Join(", ", user.Roles);
            await users.SetRolesAsync(tenantId, identifier, request.Roles, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.UserRolesChanged,
                Actor = op!.Identifier,
                Summary = $"Roles of '{identifier}' changed from [{before}] to [{string.Join(", ", request.Roles)}].",
                Detail = new Dictionary<string, string>
                {
                    ["identifier"] = identifier,
                    ["before"] = before,
                    ["after"] = string.Join(", ", request.Roles)
                }
            }, ct);

            return Results.Ok(new { identifier, roles = request.Roles });
        });

        return app;
    }
}
