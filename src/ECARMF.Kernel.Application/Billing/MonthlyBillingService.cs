using ECARMF.Kernel.Application.Identity;

namespace ECARMF.Kernel.Application.Billing;

public interface IMonthlyBillingService
{
    /// <summary>Month close, idempotent: for every Active client tenant,
    /// generates the previous calendar month's statement on its assigned
    /// plan unless one already exists. Returns how many were generated.</summary>
    Task<int> EnsureMonthlyStatementsAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Closes the month for the whole portfolio: no more remembering to
/// generate statements client by client. Each statement is metered from
/// the operational tables at generation time, so the bill and the truth
/// cannot drift apart.
/// </summary>
public class MonthlyBillingService : IMonthlyBillingService
{
    public const string DefaultPlanId = "standard";

    private readonly ITenantDirectory _tenants;
    private readonly IBillingService _billing;
    private readonly IBillingStatementStore _statements;
    private readonly IBillingPlanStore _plans;

    public MonthlyBillingService(
        ITenantDirectory tenants, IBillingService billing,
        IBillingStatementStore statements, IBillingPlanStore plans)
    {
        _tenants = tenants;
        _billing = billing;
        _statements = statements;
        _plans = plans;
    }

    public async Task<int> EnsureMonthlyStatementsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodStart = monthStart.AddMonths(-1);
        var periodEnd = monthStart;

        await _plans.EnsureDefaultPlanAsync(ct);

        var generated = 0;
        foreach (var tenant in await _tenants.GetAllAsync(ct))
        {
            if (!string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existing = await _statements.GetForTenantAsync(tenant.TenantId, 50, ct);
            if (existing.Any(s => s.PeriodStart == periodStart))
            {
                continue;
            }

            await _billing.GenerateStatementAsync(
                tenant.TenantId,
                string.IsNullOrWhiteSpace(tenant.BillingPlanId) ? DefaultPlanId : tenant.BillingPlanId!,
                periodStart, periodEnd, "system:flywheel", ct);
            generated++;
        }

        return generated;
    }
}
