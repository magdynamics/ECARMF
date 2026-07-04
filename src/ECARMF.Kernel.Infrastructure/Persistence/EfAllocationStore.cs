using System.Text.Json;
using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Domain.Capital;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfAllocationStore : IAllocationStore
{
    private readonly ECARMFDbContext _db;

    public EfAllocationStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AllocationRecommendation recommendation, CancellationToken ct = default)
    {
        _db.Allocations.Add(ToRecord(recommendation));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AllocationRecommendation?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.Allocations.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task UpdateDecisionAsync(AllocationRecommendation recommendation, CancellationToken ct = default)
    {
        var record = await _db.Allocations.FirstOrDefaultAsync(
            a => a.TenantId == recommendation.TenantId && a.Id == recommendation.Id, ct)
            ?? throw new InvalidOperationException($"Allocation '{recommendation.Id}' is not persisted.");

        record.Status = recommendation.Status;
        record.DecidedBy = recommendation.DecidedBy;
        record.DecidedAt = recommendation.DecidedAt;
        record.DecisionComment = recommendation.DecisionComment;
        record.ModifiedAmount = recommendation.ModifiedAmount;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AllocationRecommendation>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Allocations.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static AllocationRecord ToRecord(AllocationRecommendation r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        TargetReference = r.TargetReference,
        TargetAssetClass = r.TargetAssetClass,
        RecommendedAmount = r.RecommendedAmount,
        TargetInstitution = r.TargetInstitution,
        TargetJurisdiction = r.TargetJurisdiction,
        ConfidenceScore = r.ConfidenceScore,
        Reasoning = r.Reasoning,
        AssumptionsJson = JsonSerializer.Serialize(r.Assumptions),
        RiskFactorsJson = JsonSerializer.Serialize(r.RiskFactors),
        AlternativesJson = JsonSerializer.Serialize(r.AlternativesConsidered),
        SupportingScoreIdsJson = JsonSerializer.Serialize(r.SupportingScoreRecordIds),
        Tier = r.Tier.ToString(),
        Status = r.Status,
        CorrelationId = r.CorrelationId,
        CreatedAt = r.CreatedAt,
        DecidedBy = r.DecidedBy,
        DecidedAt = r.DecidedAt,
        DecisionComment = r.DecisionComment,
        ModifiedAmount = r.ModifiedAmount
    };

    private static AllocationRecommendation ToDomain(AllocationRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        TargetReference = r.TargetReference,
        TargetAssetClass = r.TargetAssetClass,
        RecommendedAmount = r.RecommendedAmount,
        TargetInstitution = r.TargetInstitution,
        TargetJurisdiction = r.TargetJurisdiction,
        ConfidenceScore = r.ConfidenceScore,
        Reasoning = r.Reasoning,
        Assumptions = JsonSerializer.Deserialize<List<string>>(r.AssumptionsJson) ?? [],
        RiskFactors = JsonSerializer.Deserialize<List<string>>(r.RiskFactorsJson) ?? [],
        AlternativesConsidered = JsonSerializer.Deserialize<List<AllocationAlternative>>(r.AlternativesJson) ?? [],
        SupportingScoreRecordIds = JsonSerializer.Deserialize<List<Guid>>(r.SupportingScoreIdsJson) ?? [],
        Tier = Enum.Parse<AutonomyTier>(r.Tier),
        Status = r.Status,
        CorrelationId = r.CorrelationId,
        CreatedAt = r.CreatedAt,
        DecidedBy = r.DecidedBy,
        DecidedAt = r.DecidedAt,
        DecisionComment = r.DecisionComment,
        ModifiedAmount = r.ModifiedAmount
    };
}
