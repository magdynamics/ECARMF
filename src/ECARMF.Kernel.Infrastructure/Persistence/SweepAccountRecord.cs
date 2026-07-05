using ECARMF.Kernel.Application.Treasury;
using ECARMF.Kernel.Domain.Treasury;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class SweepAccountRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? UnitId { get; set; }
    public string Institution { get; set; } = string.Empty;
    public string Kind { get; set; } = SweepAccountKinds.Operating;
    public string? DestinationAccountId { get; set; }
    public decimal? ApprovedThreshold { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public decimal? ProposedThreshold { get; set; }
    public DateTimeOffset? ProposedAt { get; set; }
    public string? ProposalReasoning { get; set; }
    public bool Enabled { get; set; } = true;
    public decimal? LastObservedBalance { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public DateTimeOffset? LastSweepAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfSweepAccountStore : ISweepAccountStore
{
    private readonly ECARMFDbContext _db;

    public EfSweepAccountStore(ECARMFDbContext db) => _db = db;

    public async Task<SweepAccount?> GetAsync(string tenantId, string accountId, CancellationToken ct = default)
    {
        var record = await _db.SweepAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.AccountId == accountId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<SweepAccount>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.SweepAccounts.AsNoTracking()
            .Where(a => a.TenantId == tenantId).OrderBy(a => a.Name).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SweepAccount>> GetEnabledAllTenantsAsync(CancellationToken ct = default)
    {
        var records = await _db.SweepAccounts.AsNoTracking()
            .Where(a => a.Enabled).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(SweepAccount account, CancellationToken ct = default)
    {
        _db.SweepAccounts.Add(ToRecord(account));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SweepAccount account, CancellationToken ct = default)
    {
        var record = await _db.SweepAccounts.FirstAsync(
            a => a.TenantId == account.TenantId && a.AccountId == account.AccountId, ct);
        record.Name = account.Name;
        record.UnitId = account.UnitId;
        record.Institution = account.Institution;
        record.Kind = account.Kind;
        record.DestinationAccountId = account.DestinationAccountId;
        record.ApprovedThreshold = account.ApprovedThreshold;
        record.ApprovedBy = account.ApprovedBy;
        record.ApprovedAt = account.ApprovedAt;
        record.ProposedThreshold = account.ProposedThreshold;
        record.ProposedAt = account.ProposedAt;
        record.ProposalReasoning = account.ProposalReasoning;
        record.Enabled = account.Enabled;
        record.LastObservedBalance = account.LastObservedBalance;
        record.LastObservedAt = account.LastObservedAt;
        record.LastSweepAt = account.LastSweepAt;
        record.UpdatedAt = account.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, string accountId, CancellationToken ct = default)
    {
        var record = await _db.SweepAccounts.FirstOrDefaultAsync(
            a => a.TenantId == tenantId && a.AccountId == accountId, ct);
        if (record is not null)
        {
            _db.SweepAccounts.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static SweepAccountRecord ToRecord(SweepAccount a) => new()
    {
        Id = a.Id, TenantId = a.TenantId, AccountId = a.AccountId, Name = a.Name,
        UnitId = a.UnitId, Institution = a.Institution, Kind = a.Kind,
        DestinationAccountId = a.DestinationAccountId,
        ApprovedThreshold = a.ApprovedThreshold, ApprovedBy = a.ApprovedBy, ApprovedAt = a.ApprovedAt,
        ProposedThreshold = a.ProposedThreshold, ProposedAt = a.ProposedAt, ProposalReasoning = a.ProposalReasoning,
        Enabled = a.Enabled, LastObservedBalance = a.LastObservedBalance, LastObservedAt = a.LastObservedAt,
        LastSweepAt = a.LastSweepAt, CreatedBy = a.CreatedBy, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt
    };

    private static SweepAccount ToDomain(SweepAccountRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, AccountId = r.AccountId, Name = r.Name,
        UnitId = r.UnitId, Institution = r.Institution, Kind = r.Kind,
        DestinationAccountId = r.DestinationAccountId,
        ApprovedThreshold = r.ApprovedThreshold, ApprovedBy = r.ApprovedBy, ApprovedAt = r.ApprovedAt,
        ProposedThreshold = r.ProposedThreshold, ProposedAt = r.ProposedAt, ProposalReasoning = r.ProposalReasoning,
        Enabled = r.Enabled, LastObservedBalance = r.LastObservedBalance, LastObservedAt = r.LastObservedAt,
        LastSweepAt = r.LastSweepAt, CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}
