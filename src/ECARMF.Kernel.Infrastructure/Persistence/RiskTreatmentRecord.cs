using ECARMF.Kernel.Application.Risk;
using ECARMF.Kernel.Domain.Risk;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class RiskTreatmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string RiskKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int InherentSeverity { get; set; }
    public int InherentLikelihood { get; set; }
    public string? Owner { get; set; }
    public string Strategy { get; set; } = RiskStrategies.Mitigate;
    public string Status { get; set; } = RiskTreatmentStatuses.Identified;
    public string? MitigationPlan { get; set; }
    public int? ResidualSeverity { get; set; }
    public int? ResidualLikelihood { get; set; }
    public DateTimeOffset? TargetDate { get; set; }
    public string? LinkedActionRef { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfRiskTreatmentStore : IRiskTreatmentStore
{
    private readonly ECARMFDbContext _db;

    public EfRiskTreatmentStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyList<RiskTreatment>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.RiskTreatments.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<RiskTreatment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var r = await _db.RiskTreatments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task<RiskTreatment?> GetByRiskKeyAsync(string tenantId, string riskKey, CancellationToken ct = default)
    {
        var r = await _db.RiskTreatments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RiskKey == riskKey, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task AddAsync(RiskTreatment t, CancellationToken ct = default)
    {
        _db.RiskTreatments.Add(new RiskTreatmentRecord
        {
            Id = t.Id, TenantId = t.TenantId, RiskKey = t.RiskKey, Title = t.Title, Domain = t.Domain,
            InherentSeverity = t.InherentSeverity, InherentLikelihood = t.InherentLikelihood,
            Owner = t.Owner, Strategy = t.Strategy, Status = t.Status, MitigationPlan = t.MitigationPlan,
            ResidualSeverity = t.ResidualSeverity, ResidualLikelihood = t.ResidualLikelihood,
            TargetDate = t.TargetDate, LinkedActionRef = t.LinkedActionRef,
            CreatedBy = t.CreatedBy, CreatedAt = t.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RiskTreatment t, CancellationToken ct = default)
    {
        var r = await _db.RiskTreatments.FirstAsync(x => x.TenantId == t.TenantId && x.Id == t.Id, ct);
        r.Title = t.Title; r.Owner = t.Owner; r.Strategy = t.Strategy; r.Status = t.Status;
        r.MitigationPlan = t.MitigationPlan; r.ResidualSeverity = t.ResidualSeverity;
        r.ResidualLikelihood = t.ResidualLikelihood; r.TargetDate = t.TargetDate;
        r.LinkedActionRef = t.LinkedActionRef; r.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static RiskTreatment ToDomain(RiskTreatmentRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, RiskKey = r.RiskKey, Title = r.Title, Domain = r.Domain,
        InherentSeverity = r.InherentSeverity, InherentLikelihood = r.InherentLikelihood,
        Owner = r.Owner, Strategy = r.Strategy, Status = r.Status, MitigationPlan = r.MitigationPlan,
        ResidualSeverity = r.ResidualSeverity, ResidualLikelihood = r.ResidualLikelihood,
        TargetDate = r.TargetDate, LinkedActionRef = r.LinkedActionRef,
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}
