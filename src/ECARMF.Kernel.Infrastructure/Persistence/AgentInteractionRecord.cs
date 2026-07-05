using ECARMF.Kernel.Application.Agents;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class AgentInteractionRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string ModelReference { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string AskedBy { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset AskedAt { get; set; }
    public bool? FeedbackUseful { get; set; }
    public string? FeedbackBy { get; set; }
    public DateTimeOffset? FeedbackAt { get; set; }
}

public class EfAgentInteractionStore : IAgentInteractionStore
{
    private readonly ECARMFDbContext _db;

    public EfAgentInteractionStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(AgentInteraction interaction, CancellationToken ct = default)
    {
        _db.AgentInteractions.Add(ToRecord(interaction));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AgentInteraction?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.AgentInteractions.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task UpdateFeedbackAsync(AgentInteraction interaction, CancellationToken ct = default)
    {
        var record = await _db.AgentInteractions.FirstAsync(
            i => i.TenantId == interaction.TenantId && i.Id == interaction.Id, ct);
        record.FeedbackUseful = interaction.FeedbackUseful;
        record.FeedbackBy = interaction.FeedbackBy;
        record.FeedbackAt = interaction.FeedbackAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentInteraction>> GetRecentAsync(
        string tenantId, string? agentId, int limit, CancellationToken ct = default)
    {
        var interactions = _db.AgentInteractions.AsNoTracking().Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            interactions = interactions.Where(i => i.AgentId == agentId);
        }

        var records = await interactions.OrderByDescending(i => i.AskedAt)
            .Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static AgentInteractionRecord ToRecord(AgentInteraction i) => new()
    {
        Id = i.Id, TenantId = i.TenantId, AgentId = i.AgentId,
        PackageId = i.PackageId, PackageVersion = i.PackageVersion,
        Question = i.Question, Answer = i.Answer,
        ModelReference = i.ModelReference, Provenance = i.Provenance,
        AskedBy = i.AskedBy, CorrelationId = i.CorrelationId, AskedAt = i.AskedAt,
        FeedbackUseful = i.FeedbackUseful, FeedbackBy = i.FeedbackBy, FeedbackAt = i.FeedbackAt
    };

    private static AgentInteraction ToDomain(AgentInteractionRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, AgentId = r.AgentId,
        PackageId = r.PackageId, PackageVersion = r.PackageVersion,
        Question = r.Question, Answer = r.Answer,
        ModelReference = r.ModelReference, Provenance = r.Provenance,
        AskedBy = r.AskedBy, CorrelationId = r.CorrelationId, AskedAt = r.AskedAt,
        FeedbackUseful = r.FeedbackUseful, FeedbackBy = r.FeedbackBy, FeedbackAt = r.FeedbackAt
    };
}
