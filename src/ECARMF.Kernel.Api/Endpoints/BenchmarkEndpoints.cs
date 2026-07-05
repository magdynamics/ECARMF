using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveBenchmarkRequest(
    string Name, string? Description, string Kind,
    string? MetricType, string? SubjectId, string? RecordType, string? Field,
    string ExpectationOperator, decimal ExpectedValue,
    string Severity, string NotifyRole, bool CreateTask, bool Enabled);

/// <summary>
/// Tenant expectations with triggers: "GP% >= 0.25", "amount <= 10000",
/// "openViolations <= 10". A breach raises a DeviationAlert, a notification
/// to the configured role, and optionally a review task.
/// </summary>
public static class BenchmarkEndpoints
{
    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/benchmarks");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IBenchmarkStore benchmarks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await benchmarks.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            SaveBenchmarkRequest request, HttpContext context,
            IUserStore users, IBenchmarkStore benchmarks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            // Setting business expectations is configuration of the tenant's
            // controls — the same permission that governs connector config.
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var (valid, message, benchmark) = Validate(request, tenantId, user!.Identifier);
            if (!valid) return Results.BadRequest(new { error = message });

            await benchmarks.AddAsync(benchmark!, ct);
            return Results.Created($"/api/benchmarks/{benchmark!.Id}", benchmark);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveBenchmarkRequest request, HttpContext context,
            IUserStore users, IBenchmarkStore benchmarks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var existing = await benchmarks.GetAsync(tenantId, id, ct);
            if (existing is null) return Results.NotFound();

            var (valid, message, updated) = Validate(request, tenantId, existing.CreatedBy);
            if (!valid) return Results.BadRequest(new { error = message });

            updated!.Id = existing.Id;
            updated.CreatedAt = existing.CreatedAt;
            await benchmarks.UpdateAsync(updated, ct);
            return Results.Ok(updated);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext context, IUserStore users, IBenchmarkStore benchmarks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await benchmarks.DeleteAsync(tenantId, id, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static (bool Valid, string? Message, Benchmark? Benchmark) Validate(
        SaveBenchmarkRequest request, string tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (false, "name is required.", null);
        if (request.Kind is not ("score" or "recordField"))
            return (false, "kind must be score or recordField.", null);
        if (request.Kind == "score" && string.IsNullOrWhiteSpace(request.MetricType))
            return (false, "score benchmarks require metricType (a ScoreType, e.g. KPIActual, AMLRisk).", null);
        if (request.Kind == "recordField" && (string.IsNullOrWhiteSpace(request.RecordType) || string.IsNullOrWhiteSpace(request.Field)))
            return (false, "recordField benchmarks require recordType and field.", null);
        if (!Enum.TryParse<ConditionOperator>(request.ExpectationOperator, ignoreCase: true, out var op))
            return (false, "expectationOperator must be one of: " + string.Join(", ", Enum.GetNames<ConditionOperator>()), null);
        if (request.Severity is not ("Info" or "Warning" or "Critical"))
            return (false, "severity must be Info, Warning, or Critical.", null);
        if (!RoleCatalog.RolePermissions.ContainsKey(request.NotifyRole))
            return (false, "notifyRole must be a role from the catalog.", null);

        return (true, null, new Benchmark
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description,
            Kind = request.Kind,
            MetricType = request.MetricType ?? string.Empty,
            SubjectId = request.SubjectId,
            RecordType = request.RecordType,
            Field = request.Field,
            ExpectationOperator = op,
            ExpectedValue = request.ExpectedValue,
            Severity = request.Severity,
            NotifyRole = request.NotifyRole,
            CreateTask = request.CreateTask,
            Enabled = request.Enabled,
            CreatedBy = createdBy
        });
    }
}
