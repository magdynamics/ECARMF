using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfScoreStore : IScoreStore
{
    private readonly ECARMFDbContext _db;

    public EfScoreStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(ScoreRecord score, CancellationToken ct = default)
    {
        _db.Scores.Add(new ScoreEntry
        {
            Id = score.Id,
            TenantId = score.TenantId,
            SubjectType = score.SubjectType,
            SubjectId = score.SubjectId,
            ScoreType = score.ScoreType,
            Value = score.Value,
            RuleId = score.RuleId,
            PackageId = score.PackageId,
            PackageVersion = score.PackageVersion,
            Provenance = score.Provenance,
            RiskType = score.RiskType,
            UnitRef = score.UnitRef,
            CorrelationId = score.CorrelationId,
            ComputedAt = score.ComputedAt,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(score.Metadata)
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ScoreRecord>> GetHistoryAsync(
        string tenantId, string subjectType, string subjectId, CancellationToken ct = default)
    {
        var entries = await _db.Scores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.SubjectType == subjectType && s.SubjectId == subjectId)
            .OrderBy(s => s.ComputedAt)
            .ToListAsync(ct);
        return entries.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ScoreRecord>> GetRecentAsync(
        string tenantId, int limit, string? scoreType = null, CancellationToken ct = default)
    {
        var query = _db.Scores.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(scoreType))
        {
            query = query.Where(s => s.ScoreType == scoreType);
        }

        var entries = await query
            .OrderByDescending(s => s.ComputedAt)
            .Take(limit)
            .ToListAsync(ct);
        return entries.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ScoreRecord>> GetRecentRiskAsync(
        string tenantId, int limit, CancellationToken ct = default)
    {
        var entries = await _db.Scores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.RiskType != null && s.RiskType != "")
            .OrderByDescending(s => s.ComputedAt)
            .Take(limit)
            .ToListAsync(ct);
        return entries.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ScoreRecord>> GetRecentByTypeAllTenantsAsync(
        string scoreType, int limit, CancellationToken ct = default)
    {
        var entries = await _db.Scores.AsNoTracking()
            .Where(s => s.ScoreType == scoreType)
            .OrderByDescending(s => s.ComputedAt)
            .Take(limit)
            .ToListAsync(ct);
        return entries.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ScoreRecord>> GetRecentRiskAllTenantsAsync(
        int limit, CancellationToken ct = default)
    {
        var entries = await _db.Scores.AsNoTracking()
            .Where(s => s.RiskType != null && s.RiskType != "")
            .OrderByDescending(s => s.ComputedAt)
            .Take(limit)
            .ToListAsync(ct);
        return entries.Select(ToDomain).ToList();
    }

    private static ScoreRecord ToDomain(ScoreEntry entry) => new()
    {
        Id = entry.Id,
        TenantId = entry.TenantId,
        SubjectType = entry.SubjectType,
        SubjectId = entry.SubjectId,
        ScoreType = entry.ScoreType,
        Value = entry.Value,
        RuleId = entry.RuleId,
        PackageId = entry.PackageId,
        PackageVersion = entry.PackageVersion,
        Provenance = entry.Provenance,
        RiskType = entry.RiskType,
        UnitRef = entry.UnitRef,
        CorrelationId = entry.CorrelationId,
        ComputedAt = entry.ComputedAt,
        Metadata = string.IsNullOrWhiteSpace(entry.MetadataJson) ? [] : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(entry.MetadataJson) ?? []
    };
}
