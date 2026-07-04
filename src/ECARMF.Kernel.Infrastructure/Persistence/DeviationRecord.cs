using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Domain.Analytics;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class DeviationRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string EntityReference { get; set; } = string.Empty;
    public string MetricType { get; set; } = string.Empty;
    public decimal ActualValue { get; set; }
    public decimal ExpectedValue { get; set; }
    public string ExpectedValueSource { get; set; } = string.Empty;
    public decimal VarianceMagnitude { get; set; }
    public decimal ThresholdBreached { get; set; }
    public string Severity { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public class EfDeviationStore : IDeviationStore
{
    private readonly ECARMFDbContext _db;

    public EfDeviationStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(DeviationAlert a, CancellationToken ct = default)
    {
        _db.Deviations.Add(new DeviationRecord
        {
            Id = a.Id, TenantId = a.TenantId, EntityReference = a.EntityReference,
            MetricType = a.MetricType, ActualValue = a.ActualValue, ExpectedValue = a.ExpectedValue,
            ExpectedValueSource = a.ExpectedValueSource, VarianceMagnitude = a.VarianceMagnitude,
            ThresholdBreached = a.ThresholdBreached, Severity = a.Severity,
            CorrelationId = a.CorrelationId, DetectedAt = a.DetectedAt,
            AcknowledgedBy = a.AcknowledgedBy, ResolvedAt = a.ResolvedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DeviationAlert?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var r = await _db.Deviations.AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task UpdateAsync(DeviationAlert a, CancellationToken ct = default)
    {
        var r = await _db.Deviations.FirstAsync(d => d.TenantId == a.TenantId && d.Id == a.Id, ct);
        r.AcknowledgedBy = a.AcknowledgedBy;
        r.ResolvedAt = a.ResolvedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeviationAlert>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Deviations.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderByDescending(d => d.DetectedAt).Take(limit).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static DeviationAlert ToDomain(DeviationRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, EntityReference = r.EntityReference,
        MetricType = r.MetricType, ActualValue = r.ActualValue, ExpectedValue = r.ExpectedValue,
        ExpectedValueSource = r.ExpectedValueSource, VarianceMagnitude = r.VarianceMagnitude,
        ThresholdBreached = r.ThresholdBreached, Severity = r.Severity,
        CorrelationId = r.CorrelationId, DetectedAt = r.DetectedAt,
        AcknowledgedBy = r.AcknowledgedBy, ResolvedAt = r.ResolvedAt
    };
}
