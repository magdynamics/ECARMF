using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Application.Identity;

/// <summary>Tenant-scoped investor identity profiles (Batch 2, Refinement 10).</summary>
public interface IInvestorProfileStore
{
    Task<InvestorProfile?> GetAsync(string tenantId, string userIdentifier, CancellationToken ct = default);
    Task<IReadOnlyList<InvestorProfile>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(InvestorProfile profile, CancellationToken ct = default);
    Task UpdateAsync(InvestorProfile profile, CancellationToken ct = default);
}
