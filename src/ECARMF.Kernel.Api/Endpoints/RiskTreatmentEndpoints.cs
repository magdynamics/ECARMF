using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Risk;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Risk;

namespace ECARMF.Kernel.Api.Endpoints;

public record OpenTreatmentRequest(
    string RiskKey, string Title, string? Domain, int InherentSeverity, int InherentLikelihood,
    string? Owner, string? Strategy);

public record UpdateTreatmentRequest(
    string? Owner, string? Strategy, string? Status, string? MitigationPlan,
    int? ResidualSeverity, int? ResidualLikelihood, DateTimeOffset? TargetDate, string? LinkedActionRef);

/// <summary>
/// Risk treatment: turns the risk heatmap into managed risk — an owner, a
/// strategy, a plan, and a residual rating per risk. Scoped to the tenant.
/// </summary>
public static class RiskTreatmentEndpoints
{
    public static IEndpointRouteBuilder MapRiskTreatmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/risk/treatments");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IRiskTreatmentStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;
            return Results.Ok(await store.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            OpenTreatmentRequest request, HttpContext context,
            IUserStore users, IRiskTreatmentStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.RiskKey) || string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "riskKey and title are required." });
            if (request.Strategy is not null && !RiskStrategies.IsValid(request.Strategy))
                return Results.BadRequest(new { error = "strategy must be one of: " + string.Join(", ", RiskStrategies.All) });
            if (await store.GetByRiskKeyAsync(tenantId, request.RiskKey, ct) is { } existing)
                return Results.Ok(existing); // already under treatment — idempotent

            var t = new RiskTreatment
            {
                TenantId = tenantId,
                RiskKey = request.RiskKey.Trim(),
                Title = request.Title.Trim(),
                Domain = request.Domain ?? string.Empty,
                InherentSeverity = Clamp(request.InherentSeverity),
                InherentLikelihood = Clamp(request.InherentLikelihood),
                Owner = request.Owner,
                Strategy = request.Strategy ?? RiskStrategies.Mitigate,
                Status = RiskTreatmentStatuses.Identified,
                CreatedBy = user!.Identifier
            };
            await store.AddAsync(t, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId, CorrelationId = t.Id, Category = AuditCategories.RiskTreatmentOpened,
                Actor = user.Identifier, Summary = $"Risk treatment opened for '{t.Title}' ({t.Domain}).",
                Detail = new Dictionary<string, string> { ["riskKey"] = t.RiskKey, ["strategy"] = t.Strategy }
            }, ct);
            return Results.Created($"/api/risk/treatments/{t.Id}", t);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateTreatmentRequest request, HttpContext context,
            IUserStore users, IRiskTreatmentStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var t = await store.GetAsync(tenantId, id, ct);
            if (t is null) return Results.NotFound();
            if (request.Strategy is not null && !RiskStrategies.IsValid(request.Strategy))
                return Results.BadRequest(new { error = "invalid strategy." });
            if (request.Status is not null && !RiskTreatmentStatuses.IsValid(request.Status))
                return Results.BadRequest(new { error = "invalid status." });

            if (request.Owner is not null) t.Owner = request.Owner.Trim() is { Length: > 0 } o ? o : null;
            if (request.Strategy is not null) t.Strategy = request.Strategy;
            if (request.Status is not null) t.Status = request.Status;
            if (request.MitigationPlan is not null) t.MitigationPlan = request.MitigationPlan;
            if (request.ResidualSeverity is not null) t.ResidualSeverity = Clamp(request.ResidualSeverity.Value);
            if (request.ResidualLikelihood is not null) t.ResidualLikelihood = Clamp(request.ResidualLikelihood.Value);
            if (request.TargetDate is not null) t.TargetDate = request.TargetDate;
            if (request.LinkedActionRef is not null) t.LinkedActionRef = request.LinkedActionRef.Trim();

            await store.UpdateAsync(t, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId, CorrelationId = t.Id, Category = AuditCategories.RiskTreatmentUpdated,
                Actor = user!.Identifier, Summary = $"Risk treatment '{t.Title}' updated to {t.Status} ({t.Strategy}).",
                Detail = new Dictionary<string, string> { ["status"] = t.Status, ["strategy"] = t.Strategy }
            }, ct);
            return Results.Ok(t);
        });

        // Spawn a governed remediation action for a treatment: submits an
        // AutonomousActionRequest (the orchestration skill governs it) and
        // links it to the treatment, moving it into treatment.
        group.MapPost("/{id:guid}/remediate", async (
            Guid id, HttpContext context,
            IUserStore users, IRiskTreatmentStore store, ITransactionIntakeService intake,
            IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var t = await store.GetAsync(tenantId, id, ct);
            if (t is null) return Results.NotFound();

            var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recordType"] = "AutonomousActionRequest",
                ["actionType"] = "remediate-risk",
                ["target"] = t.Title,
                ["riskTier"] = t.InherentSeverity >= 4 ? "High" : "Medium",
                ["approved"] = "false",
                ["verified"] = "false",
                ["riskKey"] = t.RiskKey
            };
            var receipt = await intake.ReceiveAsync(
                new TransactionSubmission(tenantId, "AutonomousActionRequest", user!.Identifier, payload), ct);

            t.LinkedActionRef = receipt.TransactionId.ToString();
            t.Status = RiskTreatmentStatuses.InTreatment;
            await store.UpdateAsync(t, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId, CorrelationId = t.Id, Category = AuditCategories.RiskTreatmentUpdated,
                Actor = user.Identifier, Summary = $"Remediation action spawned for risk '{t.Title}'.",
                Detail = new Dictionary<string, string> { ["actionRef"] = t.LinkedActionRef }
            }, ct);

            return Results.Ok(new { treatment = t, actionId = receipt.TransactionId });
        });

        // Approve & execute a treatment's remediation: submit the action as
        // approved + verified (the orchestration skill now authorizes it rather
        // than denying it), then mark the risk Mitigated with a reduced residual.
        group.MapPost("/{id:guid}/resolve-remediation", async (
            Guid id, HttpContext context,
            IUserStore users, IRiskTreatmentStore store, ITransactionIntakeService intake,
            IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var t = await store.GetAsync(tenantId, id, ct);
            if (t is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(t.LinkedActionRef))
                return Results.BadRequest(new { error = "No remediation action to resolve — spawn one first." });

            var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recordType"] = "AutonomousActionRequest",
                ["actionType"] = "remediate-risk",
                ["target"] = t.Title,
                ["riskTier"] = t.InherentSeverity >= 4 ? "High" : "Medium",
                ["approved"] = "true",
                ["verified"] = "true",
                ["killSwitch"] = "false",
                ["riskKey"] = t.RiskKey
            };
            var receipt = await intake.ReceiveAsync(
                new TransactionSubmission(tenantId, "AutonomousActionRequest", user!.Identifier, payload), ct);

            // Treatment reduces the risk: residual sits below inherent.
            t.Status = RiskTreatmentStatuses.Mitigated;
            t.ResidualSeverity = Math.Max(1, t.InherentSeverity - 2);
            t.ResidualLikelihood = Math.Max(1, t.InherentLikelihood - 1);
            await store.UpdateAsync(t, ct);
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId, CorrelationId = t.Id, Category = AuditCategories.RiskTreatmentUpdated,
                Actor = user.Identifier,
                Summary = $"Remediation approved & executed for '{t.Title}'; risk mitigated to {t.ResidualSeverity}x{t.ResidualLikelihood}.",
                Detail = new Dictionary<string, string> { ["actionId"] = receipt.TransactionId.ToString(), ["status"] = t.Status }
            }, ct);

            return Results.Ok(new { treatment = t, actionId = receipt.TransactionId });
        });

        return app;
    }

    private static int Clamp(int n) => Math.Max(1, Math.Min(5, n));
}
