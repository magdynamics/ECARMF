using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Billing;

namespace ECARMF.Kernel.Api.Endpoints;

public record CreatePlanRequest(
    string PlanId, string Name, string? Currency, decimal BaseMonthlyFee,
    decimal PricePerRecord, decimal PricePerDocumentArchived, decimal PricePerAiCall,
    decimal PricePerFeedRun, decimal PricePerActiveUser);

public record AssignPlanRequest(string PlanId);

public record GenerateStatementRequest(DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);

/// <summary>
/// Platform-operator billing: metered utilization per client, plans with
/// rates, and statement generation. Reuses the platform-operator guard —
/// only the operator tenant charges clients.
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/billing");

        group.MapGet("/plans", async (
            HttpContext context, IUserStore users, IBillingPlanStore plans, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            await plans.EnsureDefaultPlanAsync(ct);
            return Results.Ok(await plans.GetAllAsync(ct));
        });

        group.MapPost("/plans", async (
            CreatePlanRequest request, HttpContext context,
            IUserStore users, IBillingPlanStore plans, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.PlanId) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "planId and name are required." });
            if (await plans.GetAsync(request.PlanId, ct) is not null)
                return Results.BadRequest(new { error = $"Plan '{request.PlanId}' already exists." });

            var plan = new BillingPlan
            {
                PlanId = request.PlanId.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency,
                BaseMonthlyFee = request.BaseMonthlyFee,
                PricePerRecord = request.PricePerRecord,
                PricePerDocumentArchived = request.PricePerDocumentArchived,
                PricePerAiCall = request.PricePerAiCall,
                PricePerFeedRun = request.PricePerFeedRun,
                PricePerActiveUser = request.PricePerActiveUser
            };
            await plans.AddAsync(plan, ct);
            return Results.Created($"/api/platform/billing/plans/{plan.PlanId}", plan);
        });

        group.MapPost("/tenants/{tenantId}/plan", async (
            string tenantId, AssignPlanRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IBillingPlanStore plans,
            IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var profile = await tenants.GetAsync(tenantId, ct);
            if (profile is null) return Results.NotFound();
            if (await plans.GetAsync(request.PlanId, ct) is null)
                return Results.BadRequest(new { error = $"Plan '{request.PlanId}' does not exist." });

            profile.BillingPlanId = request.PlanId;
            await tenants.UpdateAsync(profile, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = profile.Id,
                Category = AuditCategories.BillingPlanAssigned,
                Actor = op!.Identifier,
                Summary = $"Billing plan '{request.PlanId}' assigned to tenant '{tenantId}'.",
                Detail = new Dictionary<string, string> { ["planId"] = request.PlanId }
            }, ct);

            return Results.Ok(profile);
        });

        // Live utilization for a period — the numbers a statement would bill.
        group.MapGet("/tenants/{tenantId}/usage", async (
            string tenantId, DateTimeOffset? from, DateTimeOffset? to, HttpContext context,
            IUserStore users, IUsageMeter meter, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var periodEnd = to ?? DateTimeOffset.UtcNow;
            var periodStart = from ?? new DateTimeOffset(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return Results.Ok(await meter.MeasureAsync(tenantId, periodStart, periodEnd, ct));
        });

        group.MapPost("/tenants/{tenantId}/statements", async (
            string tenantId, GenerateStatementRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IBillingPlanStore plans,
            IBillingService billing, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var profile = await tenants.GetAsync(tenantId, ct);
            if (profile is null) return Results.NotFound();
            if (request.PeriodEnd <= request.PeriodStart)
                return Results.BadRequest(new { error = "periodEnd must be after periodStart." });

            await plans.EnsureDefaultPlanAsync(ct);
            var planId = profile.BillingPlanId
                ?? (await plans.GetAllAsync(ct)).FirstOrDefault(p => p.IsDefault)?.PlanId
                ?? "standard";

            var statement = await billing.GenerateStatementAsync(
                tenantId, planId, request.PeriodStart, request.PeriodEnd, op!.Identifier, ct);
            return Results.Created($"/api/platform/billing/tenants/{tenantId}/statements/{statement.Id}", statement);
        });

        group.MapGet("/tenants/{tenantId}/statements", async (
            string tenantId, int? limit, HttpContext context,
            IUserStore users, IBillingStatementStore statements, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            return Results.Ok(await statements.GetForTenantAsync(tenantId, Math.Clamp(limit ?? 12, 1, 100), ct));
        });

        return app;
    }
}
