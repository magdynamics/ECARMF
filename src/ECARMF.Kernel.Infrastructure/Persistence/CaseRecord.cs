using System.Text.Json;
using ECARMF.Kernel.Application.Cases;
using ECARMF.Kernel.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class CaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = CaseStatuses.Open;
    public string? SkillsJson { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfCaseStore : ICaseStore
{
    private readonly ECARMFDbContext _db;

    public EfCaseStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyList<Case>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Cases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<Case?> GetAsync(string tenantId, string caseId, CancellationToken ct = default)
    {
        var record = await _db.Cases.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CaseId == caseId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task AddAsync(Case c, CancellationToken ct = default)
    {
        _db.Cases.Add(new CaseRecord
        {
            Id = c.Id,
            TenantId = c.TenantId,
            CaseId = c.CaseId,
            Name = c.Name,
            Description = c.Description,
            Status = c.Status,
            SkillsJson = c.Skills.Count > 0 ? JsonSerializer.Serialize(c.Skills) : null,
            CreatedBy = c.CreatedBy,
            CreatedAt = c.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Case c, CancellationToken ct = default)
    {
        var record = await _db.Cases.FirstAsync(r => r.TenantId == c.TenantId && r.CaseId == c.CaseId, ct);
        record.Name = c.Name;
        record.Description = c.Description;
        record.Status = c.Status;
        record.SkillsJson = c.Skills.Count > 0 ? JsonSerializer.Serialize(c.Skills) : null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static Case ToDomain(CaseRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        CaseId = r.CaseId,
        Name = r.Name,
        Description = r.Description,
        Status = r.Status,
        Skills = string.IsNullOrWhiteSpace(r.SkillsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(r.SkillsJson) ?? [],
        CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
