using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Tenancy;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ITAssetRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string? OwnerUnitId { get; set; }
    public string? Environment { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Active";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class InvestorProfileRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserIdentifier { get; set; } = string.Empty;
    public string KycStatus { get; set; } = "Pending";
    public string AmlStatus { get; set; } = "Pending";
    public string AccreditationStatus { get; set; } = "Pending";
    public Guid? OnboardingDecisionId { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfITAssetStore : IITAssetStore
{
    private readonly ECARMFDbContext _db;

    public EfITAssetStore(ECARMFDbContext db) => _db = db;

    public async Task<ITAsset?> GetAsync(string tenantId, string assetId, CancellationToken ct = default)
    {
        var record = await _db.ITAssets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.AssetId == assetId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<ITAsset>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.ITAssets.AsNoTracking()
            .Where(a => a.TenantId == tenantId).OrderBy(a => a.AssetId).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(ITAsset asset, CancellationToken ct = default)
    {
        _db.ITAssets.Add(new ITAssetRecord
        {
            Id = asset.Id, TenantId = asset.TenantId, AssetId = asset.AssetId,
            Name = asset.Name, AssetType = asset.AssetType, OwnerUnitId = asset.OwnerUnitId,
            Environment = asset.Environment, Notes = asset.Notes, Status = asset.Status,
            CreatedBy = asset.CreatedBy, CreatedAt = asset.CreatedAt, UpdatedAt = asset.UpdatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ITAsset asset, CancellationToken ct = default)
    {
        var record = await _db.ITAssets.FirstAsync(
            a => a.TenantId == asset.TenantId && a.AssetId == asset.AssetId, ct);
        record.Name = asset.Name;
        record.AssetType = asset.AssetType;
        record.OwnerUnitId = asset.OwnerUnitId;
        record.Environment = asset.Environment;
        record.Notes = asset.Notes;
        record.Status = asset.Status;
        record.UpdatedAt = asset.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static ITAsset ToDomain(ITAssetRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, AssetId = r.AssetId, Name = r.Name,
        AssetType = r.AssetType, OwnerUnitId = r.OwnerUnitId, Environment = r.Environment,
        Notes = r.Notes, Status = r.Status, CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}

public class EfInvestorProfileStore : IInvestorProfileStore
{
    private readonly ECARMFDbContext _db;

    public EfInvestorProfileStore(ECARMFDbContext db) => _db = db;

    public async Task<InvestorProfile?> GetAsync(string tenantId, string userIdentifier, CancellationToken ct = default)
    {
        var record = await _db.InvestorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserIdentifier == userIdentifier, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<InvestorProfile>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.InvestorProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId).OrderBy(p => p.UserIdentifier).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(InvestorProfile profile, CancellationToken ct = default)
    {
        _db.InvestorProfiles.Add(new InvestorProfileRecord
        {
            Id = profile.Id, TenantId = profile.TenantId, UserIdentifier = profile.UserIdentifier,
            KycStatus = profile.KycStatus, AmlStatus = profile.AmlStatus,
            AccreditationStatus = profile.AccreditationStatus,
            OnboardingDecisionId = profile.OnboardingDecisionId, Notes = profile.Notes,
            CreatedBy = profile.CreatedBy, CreatedAt = profile.CreatedAt, UpdatedAt = profile.UpdatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(InvestorProfile profile, CancellationToken ct = default)
    {
        var record = await _db.InvestorProfiles.FirstAsync(
            p => p.TenantId == profile.TenantId && p.UserIdentifier == profile.UserIdentifier, ct);
        record.KycStatus = profile.KycStatus;
        record.AmlStatus = profile.AmlStatus;
        record.AccreditationStatus = profile.AccreditationStatus;
        record.OnboardingDecisionId = profile.OnboardingDecisionId;
        record.Notes = profile.Notes;
        record.UpdatedAt = profile.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static InvestorProfile ToDomain(InvestorProfileRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, UserIdentifier = r.UserIdentifier,
        KycStatus = r.KycStatus, AmlStatus = r.AmlStatus, AccreditationStatus = r.AccreditationStatus,
        OnboardingDecisionId = r.OnboardingDecisionId, Notes = r.Notes,
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
    };
}
