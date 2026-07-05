using System.Text.Json;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class BillingPlanRecord
{
    public Guid Id { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal BaseMonthlyFee { get; set; }
    public decimal PricePerRecord { get; set; }
    public decimal PricePerDocumentArchived { get; set; }
    public decimal PricePerAiCall { get; set; }
    public decimal PricePerFeedRun { get; set; }
    public decimal PricePerActiveUser { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class BillingStatementRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string LinesJson { get; set; } = "[]";
    public decimal Total { get; set; }
    public string Status { get; set; } = "Draft";
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
}

public class EfBillingPlanStore : IBillingPlanStore
{
    private readonly ECARMFDbContext _db;

    public EfBillingPlanStore(ECARMFDbContext db) => _db = db;

    public async Task<BillingPlan?> GetAsync(string planId, CancellationToken ct = default)
    {
        var record = await _db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.PlanId == planId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<BillingPlan>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.BillingPlans.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(BillingPlan plan, CancellationToken ct = default)
    {
        _db.BillingPlans.Add(new BillingPlanRecord
        {
            Id = plan.Id,
            PlanId = plan.PlanId,
            Name = plan.Name,
            Currency = plan.Currency,
            BaseMonthlyFee = plan.BaseMonthlyFee,
            PricePerRecord = plan.PricePerRecord,
            PricePerDocumentArchived = plan.PricePerDocumentArchived,
            PricePerAiCall = plan.PricePerAiCall,
            PricePerFeedRun = plan.PricePerFeedRun,
            PricePerActiveUser = plan.PricePerActiveUser,
            IsDefault = plan.IsDefault,
            CreatedAt = plan.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureDefaultPlanAsync(CancellationToken ct = default)
    {
        if (!await _db.BillingPlans.AnyAsync(ct))
        {
            await AddAsync(new BillingPlan
            {
                PlanId = "standard",
                Name = "Standard",
                BaseMonthlyFee = 500m,
                PricePerRecord = 0.05m,
                PricePerDocumentArchived = 0.10m,
                PricePerAiCall = 0.50m,
                PricePerFeedRun = 0.25m,
                PricePerActiveUser = 25m,
                IsDefault = true
            }, ct);
        }
    }

    private static BillingPlan ToDomain(BillingPlanRecord record) => new()
    {
        Id = record.Id,
        PlanId = record.PlanId,
        Name = record.Name,
        Currency = record.Currency,
        BaseMonthlyFee = record.BaseMonthlyFee,
        PricePerRecord = record.PricePerRecord,
        PricePerDocumentArchived = record.PricePerDocumentArchived,
        PricePerAiCall = record.PricePerAiCall,
        PricePerFeedRun = record.PricePerFeedRun,
        PricePerActiveUser = record.PricePerActiveUser,
        IsDefault = record.IsDefault,
        CreatedAt = record.CreatedAt
    };
}

public class EfBillingStatementStore : IBillingStatementStore
{
    private readonly ECARMFDbContext _db;

    public EfBillingStatementStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(BillingStatement statement, CancellationToken ct = default)
    {
        _db.BillingStatements.Add(new BillingStatementRecord
        {
            Id = statement.Id,
            TenantId = statement.TenantId,
            PlanId = statement.PlanId,
            Currency = statement.Currency,
            PeriodStart = statement.PeriodStart,
            PeriodEnd = statement.PeriodEnd,
            LinesJson = JsonSerializer.Serialize(statement.Lines),
            Total = statement.Total,
            Status = statement.Status,
            GeneratedBy = statement.GeneratedBy,
            GeneratedAt = statement.GeneratedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BillingStatement>> GetForTenantAsync(
        string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.BillingStatements.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.GeneratedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return records.Select(record => new BillingStatement
        {
            Id = record.Id,
            TenantId = record.TenantId,
            PlanId = record.PlanId,
            Currency = record.Currency,
            PeriodStart = record.PeriodStart,
            PeriodEnd = record.PeriodEnd,
            Lines = JsonSerializer.Deserialize<List<BillingLineItem>>(record.LinesJson) ?? [],
            Total = record.Total,
            Status = record.Status,
            GeneratedBy = record.GeneratedBy,
            GeneratedAt = record.GeneratedAt
        }).ToList();
    }
}

/// <summary>Utilization measured straight from operational tables — the
/// billing meter can never disagree with what actually happened.</summary>
public class EfUsageMeter : IUsageMeter
{
    private readonly ECARMFDbContext _db;

    public EfUsageMeter(ECARMFDbContext db) => _db = db;

    public async Task<UsageSummary> MeasureAsync(
        string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct = default)
    {
        var records = await _db.Transactions.AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId && t.ReceivedAt >= periodStart && t.ReceivedAt <= periodEnd, ct);

        var documents = await _db.SourceDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ArchivedAt >= periodStart && d.ArchivedAt <= periodEnd)
            .Select(d => d.SizeBytes)
            .ToListAsync(ct);

        var briefs = await _db.AdvisorBriefs.AsNoTracking()
            .CountAsync(b => b.TenantId == tenantId && b.CreatedAt >= periodStart && b.CreatedAt <= periodEnd, ct);

        var llmExtractions = await _db.SourceDocuments.AsNoTracking()
            .CountAsync(d => d.TenantId == tenantId
                             && d.ArchivedAt >= periodStart && d.ArchivedAt <= periodEnd
                             && d.ExtractionBackend != null && d.ExtractionBackend.StartsWith("llm"), ct);

        var feedRuns = await _db.FeedRuns.AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId && r.StartedAt >= periodStart && r.StartedAt <= periodEnd, ct);

        var activeUsers = await _db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && !u.IsSystemActor && u.Status == "Active", ct);

        return new UsageSummary(
            tenantId, periodStart, periodEnd,
            records, documents.Count, documents.Sum(),
            briefs + llmExtractions, feedRuns, activeUsers);
    }
}
