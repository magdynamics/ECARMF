using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Treasury;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Treasury;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveSweepAccountRequest(
    string AccountId, string Name, string? UnitId, string Institution,
    string Kind, string? DestinationAccountId, bool Enabled);

public record ApproveThresholdRequest(decimal? OverrideValue);

public record ObserveBalanceRequest(decimal Balance);

/// <summary>
/// AI Treasury (Universal Dental Requirement 8): rolling threshold
/// proposals are Recommend-Only; sweep execution against a standing
/// approved threshold is Autonomous; payroll accounts alert, never sweep.
/// </summary>
public static class TreasuryEndpoints
{
    public static IEndpointRouteBuilder MapTreasuryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/treasury/accounts");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, ISweepAccountStore accounts, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await accounts.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            SaveSweepAccountRequest request, HttpContext context,
            IUserStore users, ISweepAccountStore accounts, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "accountId and name are required." });
            if (request.Kind is not (SweepAccountKinds.Operating or SweepAccountKinds.Payroll))
                return Results.BadRequest(new { error = "kind must be Operating or Payroll." });
            if (await accounts.GetAsync(tenantId, request.AccountId.Trim(), ct) is not null)
                return Results.BadRequest(new { error = $"Account '{request.AccountId}' already exists." });

            var account = new SweepAccount
            {
                TenantId = tenantId,
                AccountId = request.AccountId.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                UnitId = string.IsNullOrWhiteSpace(request.UnitId) ? null : request.UnitId.Trim(),
                Institution = request.Institution.Trim(),
                Kind = request.Kind,
                DestinationAccountId = string.IsNullOrWhiteSpace(request.DestinationAccountId)
                    ? null : request.DestinationAccountId.Trim(),
                Enabled = request.Enabled,
                CreatedBy = user!.Identifier
            };
            await accounts.AddAsync(account, ct);
            return Results.Created($"/api/treasury/accounts/{account.AccountId}", account);
        });

        group.MapDelete("/{accountId}", async (
            string accountId, HttpContext context,
            IUserStore users, ISweepAccountStore accounts, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await accounts.DeleteAsync(tenantId, accountId, ct);
            return Results.NoContent();
        });

        // Approving a threshold is an approval decision, not configuration.
        group.MapPost("/{accountId}/approve-threshold", async (
            string accountId, ApproveThresholdRequest request, HttpContext context,
            IUserStore users, ITreasurySweepService treasury, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;

            try
            {
                return Results.Ok(await treasury.ApproveThresholdAsync(
                    tenantId, accountId, user!.Identifier, request.OverrideValue, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // Balance observations arrive from bank feeds when live; this
        // endpoint is that same path, callable by feed or human.
        group.MapPost("/{accountId}/observe", async (
            string accountId, ObserveBalanceRequest request, HttpContext context,
            IUserStore users, ITreasurySweepService treasury, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            if (request.Balance < 0)
                return Results.BadRequest(new { error = "balance cannot be negative." });

            try
            {
                return Results.Ok(await treasury.ObserveBalanceAsync(
                    tenantId, accountId, request.Balance, user!.Identifier, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // On-demand AI treasury pass (the scheduler runs the same thing).
        group.MapPost("/recalculate", async (
            HttpContext context, IUserStore users, ITreasurySweepService treasury, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var proposals = await treasury.RecalculateThresholdsAsync(tenantId, DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { proposals });
        });

        return app;
    }
}
