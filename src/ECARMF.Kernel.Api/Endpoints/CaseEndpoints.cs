using System.Text.RegularExpressions;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Cases;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Cases;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record CreateCaseRequest(string CaseId, string Name, string? Description, List<string>? Skills);
public record SetCaseStatusRequest(string Status);

/// <summary>
/// Cases / projects: a cross-cutting grouping of a tenant's records so they can
/// be monitored and compared case-by-case with the full set of controls.
/// Scoped to the calling tenant.
/// </summary>
public static class CaseEndpoints
{
    private static readonly Regex Slug = new("^[a-z0-9][a-z0-9-]{1,118}[a-z0-9]$", RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cases");

        // Every case with its metrics, for side-by-side comparison.
        group.MapGet("/", async (
            HttpContext context, IUserStore users, ICaseAnalysisService analysis, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await analysis.CompareAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            CreateCaseRequest request, HttpContext context,
            IUserStore users, ICaseStore cases, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var caseId = request.CaseId?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!Slug.IsMatch(caseId))
                return Results.BadRequest(new { error = "caseId must be a 3-120 char lowercase slug (a-z, 0-9, hyphens)." });
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });
            if (await cases.GetAsync(tenantId, caseId, ct) is not null)
                return Results.BadRequest(new { error = $"Case '{caseId}' already exists." });

            var c = new Case
            {
                TenantId = tenantId,
                CaseId = caseId,
                Name = request.Name.Trim(),
                Description = request.Description,
                Skills = request.Skills ?? [],
                CreatedBy = user!.Identifier
            };
            await cases.AddAsync(c, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = c.Id,
                Category = AuditCategories.CaseCreated,
                Actor = user.Identifier,
                Summary = $"Case '{c.Name}' ({caseId}) opened.",
                Detail = new Dictionary<string, string> { ["caseId"] = caseId, ["name"] = c.Name }
            }, ct);

            return Results.Created($"/api/cases/{caseId}", c);
        });

        group.MapPost("/{caseId}/status", async (
            string caseId, SetCaseStatusRequest request, HttpContext context,
            IUserStore users, ICaseStore cases, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            if (!CaseStatuses.IsValid(request.Status))
                return Results.BadRequest(new { error = "status must be Open or Closed." });
            var c = await cases.GetAsync(tenantId, caseId, ct);
            if (c is null) return Results.NotFound();

            c.Status = CaseStatuses.IsValid(request.Status) && string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase)
                ? CaseStatuses.Closed : CaseStatuses.Open;
            await cases.UpdateAsync(c, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = c.Id,
                Category = AuditCategories.CaseStatusChanged,
                Actor = user!.Identifier,
                Summary = $"Case '{caseId}' set to {c.Status}.",
                Detail = new Dictionary<string, string> { ["caseId"] = caseId, ["status"] = c.Status }
            }, ct);

            return Results.Ok(c);
        });

        return app;
    }
}
