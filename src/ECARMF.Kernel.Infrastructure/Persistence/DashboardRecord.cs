using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>
/// Runtime-configurable dashboard definition. Deliberately NOT
/// package-versioned: this is live, editable configuration a user changes
/// through the admin UI, unlike the versioned-package extensibility used
/// everywhere else in the kernel.
/// </summary>
public class DashboardRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Widget definitions (type, title, query) as a JSON array.</summary>
    public string WidgetsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IDashboardStore
{
    Task<IReadOnlyList<DashboardRecord>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<DashboardRecord> EnsureDefaultAsync(string tenantId, CancellationToken ct = default);
    Task<DashboardRecord?> UpdateWidgetsAsync(string tenantId, Guid id, string widgetsJson, CancellationToken ct = default);
}

public class EfDashboardStore : IDashboardStore
{
    private const string DefaultWidgets = """
    [
      {"id":"w1","type":"kpiTiles","title":"Key numbers"},
      {"id":"w2","type":"outcomeBreakdown","title":"Outcome breakdown"},
      {"id":"w3","type":"scoreAverages","title":"Average score by type"},
      {"id":"w4","type":"okrAttainment","title":"OKR attainment by venture"},
      {"id":"w5","type":"deviationFeed","title":"Deviation alerts"},
      {"id":"w6","type":"recentScores","title":"Recent scores"}
    ]
    """;

    private readonly ECARMFDbContext _db;

    public EfDashboardStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyList<DashboardRecord>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureDefaultAsync(tenantId, ct);
        return await _db.Dashboards.AsNoTracking()
            .Where(d => d.TenantId == tenantId).OrderBy(d => d.Name).ToListAsync(ct);
    }

    public async Task<DashboardRecord> EnsureDefaultAsync(string tenantId, CancellationToken ct = default)
    {
        var existing = await _db.Dashboards.FirstOrDefaultAsync(d => d.TenantId == tenantId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new DashboardRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Overview",
            WidgetsJson = DefaultWidgets,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Dashboards.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<DashboardRecord?> UpdateWidgetsAsync(string tenantId, Guid id, string widgetsJson, CancellationToken ct = default)
    {
        var record = await _db.Dashboards.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        if (record is null)
        {
            return null;
        }

        record.WidgetsJson = widgetsJson;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return record;
    }
}
