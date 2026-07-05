using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Domain.Capital;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class FundingSourceRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? InvestorUserId { get; set; }
    public string? Institution { get; set; }
    public decimal? CommitmentAmount { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class FundingEventRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid FundingSourceId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? MilestoneReference { get; set; }
    public decimal? PercentCompleteClaimed { get; set; }
    public decimal Amount { get; set; }
    public string? DocumentationReference { get; set; }
    public string? VerificationNote { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    public DateTimeOffset? DisbursedAt { get; set; }
}

public class EfFundingSourceStore : IFundingSourceStore
{
    private readonly ECARMFDbContext _db;

    public EfFundingSourceStore(ECARMFDbContext db) => _db = db;

    public async Task<FundingSource?> GetAsync(string tenantId, string sourceId, CancellationToken ct = default)
    {
        var record = await _db.FundingSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SourceId == sourceId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<FundingSource>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.FundingSources.AsNoTracking()
            .Where(s => s.TenantId == tenantId).OrderBy(s => s.SourceId).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(FundingSource source, CancellationToken ct = default)
    {
        _db.FundingSources.Add(ToRecord(source));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(FundingSource source, CancellationToken ct = default)
    {
        var record = await _db.FundingSources.FirstAsync(
            s => s.TenantId == source.TenantId && s.Id == source.Id, ct);
        record.Name = source.Name;
        record.Institution = source.Institution;
        record.CommitmentAmount = source.CommitmentAmount;
        record.Notes = source.Notes;
        record.UpdatedAt = source.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static FundingSourceRecord ToRecord(FundingSource s) => new()
    {
        Id = s.Id, TenantId = s.TenantId, SourceId = s.SourceId, UnitId = s.UnitId,
        Kind = s.Kind, Name = s.Name, InvestorUserId = s.InvestorUserId, Institution = s.Institution,
        CommitmentAmount = s.CommitmentAmount, Notes = s.Notes,
        CreatedBy = s.CreatedBy, CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt
    };

    private static FundingSource ToDomain(FundingSourceRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, SourceId = r.SourceId, UnitId = r.UnitId,
        Kind = r.Kind, Name = r.Name, InvestorUserId = r.InvestorUserId, Institution = r.Institution,
        CommitmentAmount = r.CommitmentAmount, Notes = r.Notes,
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}

public class EfFundingEventStore : IFundingEventStore
{
    private readonly ECARMFDbContext _db;

    public EfFundingEventStore(ECARMFDbContext db) => _db = db;

    public async Task<FundingEvent?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.FundingEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<FundingEvent>> GetBySourceAsync(
        string tenantId, Guid fundingSourceId, CancellationToken ct = default)
    {
        var records = await _db.FundingEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.FundingSourceId == fundingSourceId)
            .OrderByDescending(e => e.RequestedAt).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(FundingEvent fundingEvent, CancellationToken ct = default)
    {
        _db.FundingEvents.Add(ToRecord(fundingEvent));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(FundingEvent fundingEvent, CancellationToken ct = default)
    {
        var record = await _db.FundingEvents.FirstAsync(
            e => e.TenantId == fundingEvent.TenantId && e.Id == fundingEvent.Id, ct);
        record.Status = fundingEvent.Status;
        record.VerificationNote = fundingEvent.VerificationNote;
        record.DecidedBy = fundingEvent.DecidedBy;
        record.DecidedAt = fundingEvent.DecidedAt;
        record.DecisionComment = fundingEvent.DecisionComment;
        record.DisbursedAt = fundingEvent.DisbursedAt;
        await _db.SaveChangesAsync(ct);
    }

    private static FundingEventRecord ToRecord(FundingEvent e) => new()
    {
        Id = e.Id, TenantId = e.TenantId, FundingSourceId = e.FundingSourceId,
        EventType = e.EventType, MilestoneReference = e.MilestoneReference,
        PercentCompleteClaimed = e.PercentCompleteClaimed, Amount = e.Amount,
        DocumentationReference = e.DocumentationReference, VerificationNote = e.VerificationNote,
        Status = e.Status, RequestedBy = e.RequestedBy, RequestedAt = e.RequestedAt,
        DecidedBy = e.DecidedBy, DecidedAt = e.DecidedAt,
        DecisionComment = e.DecisionComment, DisbursedAt = e.DisbursedAt
    };

    private static FundingEvent ToDomain(FundingEventRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, FundingSourceId = r.FundingSourceId,
        EventType = r.EventType, MilestoneReference = r.MilestoneReference,
        PercentCompleteClaimed = r.PercentCompleteClaimed, Amount = r.Amount,
        DocumentationReference = r.DocumentationReference, VerificationNote = r.VerificationNote,
        Status = r.Status, RequestedBy = r.RequestedBy, RequestedAt = r.RequestedAt,
        DecidedBy = r.DecidedBy, DecidedAt = r.DecidedAt,
        DecisionComment = r.DecisionComment, DisbursedAt = r.DisbursedAt
    };
}
