using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;

namespace ECARMF.Kernel.Application.Analytics;

public sealed record ActionItem(
    string TenantId, string TenantName, string Type, string Title, string Detail, int Urgency, string Tab);

public sealed record PlatformActions(int Total, int Urgent, IReadOnlyList<ActionItem> Items);

public interface IPlatformActionService
{
    Task<PlatformActions> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// The operator's action center: rolls the whole client base into one ranked
/// to-do — critical risks that need attention and renewals coming due — so an
/// operator works a queue instead of hunting screen by screen.
/// </summary>
public class PlatformActionService : IPlatformActionService
{
    private readonly IPlatformRiskService _risk;
    private readonly IRenewalStore _renewals;
    private readonly ITenantDirectory _tenants;

    public PlatformActionService(IPlatformRiskService risk, IRenewalStore renewals, ITenantDirectory tenants)
    {
        _risk = risk;
        _renewals = renewals;
        _tenants = tenants;
    }

    public async Task<PlatformActions> ListAsync(CancellationToken ct = default)
    {
        var names = (await _tenants.GetAllAsync(ct))
            .ToDictionary(t => t.TenantId, t => t.Name, StringComparer.OrdinalIgnoreCase);
        var items = new List<ActionItem>();

        // Critical risks per tenant.
        var overview = await _risk.OverviewAsync(ct);
        foreach (var t in overview.Tenants.Where(t => t.Critical > 0))
        {
            items.Add(new ActionItem(
                t.TenantId, t.Name, "Critical risk",
                $"{t.Critical} risk(s) in the critical zone",
                $"top domains: {string.Join(", ", t.TopDomains)}",
                Math.Min(100, 55 + t.Critical * 4), "risk"));
        }

        // Renewals coming due (or overdue) across all tenants.
        var now = DateTimeOffset.UtcNow;
        var soon = now.AddDays(45);
        foreach (var r in (await _renewals.GetActiveAllTenantsAsync(ct)).Where(r => r.DueDate <= soon))
        {
            var days = (int)Math.Ceiling((r.DueDate - now).TotalDays);
            var urgency = days < 0 ? 100 : days <= 7 ? 92 : days <= 21 ? 75 : 58;
            var detail = days < 0 ? $"overdue by {-days} day(s) · {r.Category}" : $"due in {days} day(s) · {r.Category}";
            items.Add(new ActionItem(
                r.TenantId, names.GetValueOrDefault(r.TenantId, r.TenantId),
                "Renewal", r.Name, detail, urgency, "renewals"));
        }

        var ranked = items.OrderByDescending(i => i.Urgency).ThenBy(i => i.TenantName).ToList();
        return new PlatformActions(ranked.Count, ranked.Count(i => i.Urgency >= 90), ranked);
    }
}
