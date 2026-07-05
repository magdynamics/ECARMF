using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class RenewalRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? Counterparty { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public int? RecurrenceMonths { get; set; }

    /// <summary>Warning ladder as CSV of days, e.g. "90,30,7".</summary>
    public string LeadTimeDaysCsv { get; set; } = string.Empty;

    public decimal? RequiredUnits { get; set; }
    public decimal CompletedUnits { get; set; }
    public string? UnitLabel { get; set; }
    public string NotifyRole { get; set; } = string.Empty;
    public bool CreateTask { get; set; }
    public string Status { get; set; } = RenewalStatuses.Active;
    public int? LastAlertedThresholdDays { get; set; }
    public int RenewalCount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastRenewedAt { get; set; }
}

public class EfRenewalStore : IRenewalStore
{
    private readonly ECARMFDbContext _db;

    public EfRenewalStore(ECARMFDbContext db) => _db = db;

    public async Task<RenewalCommitment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.Renewals.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<RenewalCommitment>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Renewals.AsNoTracking()
            .Where(r => r.TenantId == tenantId).OrderBy(r => r.DueDate).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<RenewalCommitment>> GetActiveAllTenantsAsync(CancellationToken ct = default)
    {
        var records = await _db.Renewals.AsNoTracking()
            .Where(r => r.Status == RenewalStatuses.Active).OrderBy(r => r.DueDate).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        _db.Renewals.Add(ToRecord(renewal));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RenewalCommitment renewal, CancellationToken ct = default)
    {
        var record = await _db.Renewals.FirstAsync(
            r => r.TenantId == renewal.TenantId && r.Id == renewal.Id, ct);
        var updated = ToRecord(renewal);
        record.Name = updated.Name;
        record.Category = updated.Category;
        record.SubjectType = updated.SubjectType;
        record.SubjectId = updated.SubjectId;
        record.Counterparty = updated.Counterparty;
        record.Reference = updated.Reference;
        record.Notes = updated.Notes;
        record.DueDate = updated.DueDate;
        record.RecurrenceMonths = updated.RecurrenceMonths;
        record.LeadTimeDaysCsv = updated.LeadTimeDaysCsv;
        record.RequiredUnits = updated.RequiredUnits;
        record.CompletedUnits = updated.CompletedUnits;
        record.UnitLabel = updated.UnitLabel;
        record.NotifyRole = updated.NotifyRole;
        record.CreateTask = updated.CreateTask;
        record.Status = updated.Status;
        record.LastAlertedThresholdDays = updated.LastAlertedThresholdDays;
        record.RenewalCount = updated.RenewalCount;
        record.UpdatedAt = updated.UpdatedAt ?? DateTimeOffset.UtcNow;
        record.LastRenewedAt = updated.LastRenewedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.Renewals.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Id == id, ct);
        if (record is not null)
        {
            _db.Renewals.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static RenewalRecord ToRecord(RenewalCommitment renewal) => new()
    {
        Id = renewal.Id,
        TenantId = renewal.TenantId,
        Name = renewal.Name,
        Category = renewal.Category,
        SubjectType = renewal.SubjectType,
        SubjectId = renewal.SubjectId,
        Counterparty = renewal.Counterparty,
        Reference = renewal.Reference,
        Notes = renewal.Notes,
        DueDate = renewal.DueDate,
        RecurrenceMonths = renewal.RecurrenceMonths,
        LeadTimeDaysCsv = string.Join(",", renewal.LeadTimeDays),
        RequiredUnits = renewal.RequiredUnits,
        CompletedUnits = renewal.CompletedUnits,
        UnitLabel = renewal.UnitLabel,
        NotifyRole = renewal.NotifyRole,
        CreateTask = renewal.CreateTask,
        Status = renewal.Status,
        LastAlertedThresholdDays = renewal.LastAlertedThresholdDays,
        RenewalCount = renewal.RenewalCount,
        CreatedBy = renewal.CreatedBy,
        CreatedAt = renewal.CreatedAt,
        UpdatedAt = renewal.UpdatedAt,
        LastRenewedAt = renewal.LastRenewedAt
    };

    private static RenewalCommitment ToDomain(RenewalRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Name = record.Name,
        Category = record.Category,
        SubjectType = record.SubjectType,
        SubjectId = record.SubjectId,
        Counterparty = record.Counterparty,
        Reference = record.Reference,
        Notes = record.Notes,
        DueDate = record.DueDate,
        RecurrenceMonths = record.RecurrenceMonths,
        LeadTimeDays = record.LeadTimeDaysCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToArray(),
        RequiredUnits = record.RequiredUnits,
        CompletedUnits = record.CompletedUnits,
        UnitLabel = record.UnitLabel,
        NotifyRole = record.NotifyRole,
        CreateTask = record.CreateTask,
        Status = record.Status,
        LastAlertedThresholdDays = record.LastAlertedThresholdDays,
        RenewalCount = record.RenewalCount,
        CreatedBy = record.CreatedBy,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        LastRenewedAt = record.LastRenewedAt
    };
}
