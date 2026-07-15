using ECARMF.Kernel.Application.Packages;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Persisted platform-admin override of a skill's packaging/price.</summary>
public class SkillSettingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SkillId { get; set; } = string.Empty;
    public string Packaging { get; set; } = SkillPackaging.Essential;
    public decimal MonthlyPrice { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class EfSkillSettingStore : ISkillSettingStore
{
    private readonly ECARMFDbContext _db;

    public EfSkillSettingStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, SkillSetting>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.SkillSettings.AsNoTracking().ToListAsync(ct);
        return records.ToDictionary(
            r => r.SkillId,
            r => new SkillSetting(r.SkillId, r.Packaging, r.MonthlyPrice),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task UpsertAsync(SkillSetting setting, string actor, CancellationToken ct = default)
    {
        var record = await _db.SkillSettings.FirstOrDefaultAsync(r => r.SkillId == setting.SkillId, ct);
        if (record is null)
        {
            _db.SkillSettings.Add(new SkillSettingRecord
            {
                SkillId = setting.SkillId,
                Packaging = setting.Packaging,
                MonthlyPrice = setting.MonthlyPrice,
                UpdatedBy = actor,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            record.Packaging = setting.Packaging;
            record.MonthlyPrice = setting.MonthlyPrice;
            record.UpdatedBy = actor;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
