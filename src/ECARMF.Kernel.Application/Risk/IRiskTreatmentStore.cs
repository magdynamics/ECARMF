using ECARMF.Kernel.Domain.Risk;

namespace ECARMF.Kernel.Application.Risk;

/// <summary>Persistence for risk treatments, tenant-scoped.</summary>
public interface IRiskTreatmentStore
{
    Task<IReadOnlyList<RiskTreatment>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<RiskTreatment?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<RiskTreatment?> GetByRiskKeyAsync(string tenantId, string riskKey, CancellationToken ct = default);
    Task AddAsync(RiskTreatment treatment, CancellationToken ct = default);
    Task UpdateAsync(RiskTreatment treatment, CancellationToken ct = default);
}
