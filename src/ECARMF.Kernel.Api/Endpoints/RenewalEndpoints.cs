using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveRenewalRequest(
    string Name, string Category, string? Counterparty, string? Reference, string? Notes,
    DateTimeOffset DueDate, int? RecurrenceMonths, int[]? LeadTimeDays,
    string NotifyRole, bool CreateTask);

/// <summary>
/// Renewal commitments: dated obligations (licenses, insurance, loans,
/// leases, corporate registrations) the kernel watches so nothing lapses
/// unannounced. The monitor escalates Info → Warning → Critical as the due
/// date approaches; marking renewed advances recurring commitments a cycle.
/// </summary>
public static class RenewalEndpoints
{
    public static IEndpointRouteBuilder MapRenewalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/renewals");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await renewals.GetAllAsync(tenantId, ct));
        });

        group.MapGet("/upcoming", async (
            int? days, HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var horizon = DateTimeOffset.UtcNow.AddDays(days is > 0 ? days.Value : 30);
            var upcoming = (await renewals.GetAllAsync(tenantId, ct))
                .Where(r => r.Status == RenewalStatuses.Active && r.DueDate <= horizon)
                .OrderBy(r => r.DueDate)
                .ToList();
            return Results.Ok(upcoming);
        });

        group.MapPost("/", async (
            SaveRenewalRequest request, HttpContext context,
            IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            // Stating obligations is configuration of the tenant's controls,
            // the same permission that governs benchmarks and connectors.
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var (valid, message, renewal) = Validate(request, tenantId, user!.Identifier);
            if (!valid) return Results.BadRequest(new { error = message });

            await renewals.AddAsync(renewal!, ct);
            return Results.Created($"/api/renewals/{renewal!.Id}", renewal);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveRenewalRequest request, HttpContext context,
            IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var existing = await renewals.GetAsync(tenantId, id, ct);
            if (existing is null) return Results.NotFound();

            var (valid, message, updated) = Validate(request, tenantId, existing.CreatedBy);
            if (!valid) return Results.BadRequest(new { error = message });

            updated!.Id = existing.Id;
            updated.CreatedAt = existing.CreatedAt;
            updated.Status = existing.Status;
            updated.RenewalCount = existing.RenewalCount;
            updated.LastRenewedAt = existing.LastRenewedAt;
            // A moved due date restarts the warning ladder.
            updated.LastAlertedThresholdDays =
                updated.DueDate == existing.DueDate ? existing.LastAlertedThresholdDays : null;
            await renewals.UpdateAsync(updated, ct);
            return Results.Ok(updated);
        });

        group.MapPost("/evaluate", async (
            HttpContext context, IUserStore users, IRenewalMonitor monitor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            // On-demand pass over this tenant's ladder — the hourly monitor
            // does the same thing platform-wide.
            var raised = await monitor.EvaluateAsync(tenantId, DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { raised });
        });

        group.MapPost("/{id:guid}/renewed", async (
            Guid id, HttpContext context, IUserStore users,
            IRenewalStore renewals, IRenewalMonitor monitor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var renewal = await monitor.MarkRenewedAsync(tenantId, id, user!.Identifier, ct);
            return renewal is null ? Results.NotFound() : Results.Ok(renewal);
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id, HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var renewal = await renewals.GetAsync(tenantId, id, ct);
            if (renewal is null) return Results.NotFound();

            renewal.Status = RenewalStatuses.Cancelled;
            renewal.UpdatedAt = DateTimeOffset.UtcNow;
            await renewals.UpdateAsync(renewal, ct);
            return Results.Ok(renewal);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await renewals.DeleteAsync(tenantId, id, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static (bool Valid, string? Message, RenewalCommitment? Renewal) Validate(
        SaveRenewalRequest request, string tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (false, "name is required.", null);
        if (!RenewalCategories.All.Contains(request.Category, StringComparer.OrdinalIgnoreCase))
            return (false, "category must be one of: " + string.Join(", ", RenewalCategories.All), null);
        if (request.DueDate == default)
            return (false, "dueDate is required.", null);
        if (request.RecurrenceMonths is <= 0)
            return (false, "recurrenceMonths must be positive (or omitted for a one-time obligation).", null);
        if (!RoleCatalog.RolePermissions.ContainsKey(request.NotifyRole))
            return (false, "notifyRole must be a role from the catalog.", null);

        var ladder = (request.LeadTimeDays is { Length: > 0 } ? request.LeadTimeDays : new[] { 90, 30, 7 })
            .Where(d => d >= 0).Distinct().OrderByDescending(d => d).ToArray();
        if (ladder.Length == 0)
            return (false, "leadTimeDays must contain at least one non-negative day count.", null);

        var category = RenewalCategories.All.First(c =>
            string.Equals(c, request.Category, StringComparison.OrdinalIgnoreCase));

        return (true, null, new RenewalCommitment
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Category = category,
            Counterparty = request.Counterparty,
            Reference = request.Reference,
            Notes = request.Notes,
            DueDate = request.DueDate,
            RecurrenceMonths = request.RecurrenceMonths,
            LeadTimeDays = ladder,
            NotifyRole = request.NotifyRole,
            CreateTask = request.CreateTask,
            CreatedBy = createdBy
        });
    }
}
