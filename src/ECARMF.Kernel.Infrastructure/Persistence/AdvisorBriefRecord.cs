using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Domain.Advisor;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class AdvisorBriefRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string RecommendationsJson { get; set; } = "[]";
    public string ModelReference { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool? FeedbackUseful { get; set; }
    public string? FeedbackBy { get; set; }
    public DateTimeOffset? FeedbackAt { get; set; }
}

public class EfAdvisorStore : IAdvisorStore
{
    private readonly ECARMFDbContext _db;

    public EfAdvisorStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(AdvisorBrief brief, CancellationToken ct = default)
    {
        _db.AdvisorBriefs.Add(new AdvisorBriefRecord
        {
            Id = brief.Id,
            TenantId = brief.TenantId,
            Title = brief.Title,
            ExecutiveSummary = brief.ExecutiveSummary,
            RecommendationsJson = JsonSerializer.Serialize(brief.Recommendations),
            ModelReference = brief.ModelReference,
            Provenance = brief.Provenance,
            CorrelationId = brief.CorrelationId,
            RequestedBy = brief.RequestedBy,
            CreatedAt = brief.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AdvisorBrief?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.AdvisorBriefs.AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task UpdateFeedbackAsync(AdvisorBrief brief, CancellationToken ct = default)
    {
        var record = await _db.AdvisorBriefs.FirstAsync(
            b => b.TenantId == brief.TenantId && b.Id == brief.Id, ct);
        record.FeedbackUseful = brief.FeedbackUseful;
        record.FeedbackBy = brief.FeedbackBy;
        record.FeedbackAt = brief.FeedbackAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdvisorBrief>> GetRecentAsync(
        string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.AdvisorBriefs.AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static AdvisorBrief ToDomain(AdvisorBriefRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Title = record.Title,
        ExecutiveSummary = record.ExecutiveSummary,
        Recommendations = string.IsNullOrWhiteSpace(record.RecommendationsJson)
            ? []
            : JsonSerializer.Deserialize<List<AdvisorRecommendation>>(record.RecommendationsJson) ?? [],
        ModelReference = record.ModelReference,
        Provenance = record.Provenance,
        CorrelationId = record.CorrelationId,
        RequestedBy = record.RequestedBy,
        CreatedAt = record.CreatedAt,
        FeedbackUseful = record.FeedbackUseful,
        FeedbackBy = record.FeedbackBy,
        FeedbackAt = record.FeedbackAt
    };
}
