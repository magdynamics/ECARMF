using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Packages;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>EF Core implementation of the package persistence port. The full
/// manifest is stored as a JSON document alongside queryable identity columns.
/// Every operation is tenant-scoped except the rehydration read.</summary>
public class EfPackageStore : IPackageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ECARMFDbContext _db;

    public EfPackageStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        return _db.KnowledgePackages.AnyAsync(
            p => p.TenantId == tenantId && p.PackageId == packageId && p.PackageVersion == packageVersion, ct);
    }

    public async Task AddAsync(string tenantId, KnowledgePackageManifest manifest, PackageLoadState state, string? statusDetail, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        _db.KnowledgePackages.Add(new KnowledgePackageRecord
        {
            Id = manifest.EntityId == Guid.Empty ? Guid.NewGuid() : manifest.EntityId,
            TenantId = tenantId,
            PackageId = manifest.PackageId,
            Name = manifest.Name,
            PackageVersion = manifest.PackageVersion,
            Publisher = manifest.Publisher,
            Description = manifest.Description,
            Owner = manifest.Owner,
            Status = state.ToString(),
            StatusDetail = statusDetail,
            CreatedAt = now,
            UpdatedAt = now,
            ManifestJson = JsonSerializer.Serialize(manifest, JsonOptions)
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<StoredPackage?> GetAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        var record = await _db.KnowledgePackages.AsNoTracking().FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.PackageId == packageId && p.PackageVersion == packageVersion, ct);

        return record is null ? null : ToStoredPackage(record);
    }

    public async Task UpdateStateAsync(string tenantId, string packageId, string packageVersion, PackageLoadState state, string? statusDetail, CancellationToken ct = default)
    {
        var record = await _db.KnowledgePackages.FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.PackageId == packageId && p.PackageVersion == packageVersion, ct)
            ?? throw new InvalidOperationException(
                $"Package '{packageId}' version '{packageVersion}' is not persisted for tenant '{tenantId}'.");

        record.Status = state.ToString();
        record.StatusDetail = statusDetail;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<StoredPackage>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.KnowledgePackages.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);
        return records.Select(ToStoredPackage).ToList();
    }

    public async Task<IReadOnlyList<StoredPackage>> GetByStateAsync(string tenantId, PackageLoadState state, CancellationToken ct = default)
    {
        var status = state.ToString();
        var records = await _db.KnowledgePackages.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Status == status)
            .ToListAsync(ct);
        return records.Select(ToStoredPackage).ToList();
    }

    public async Task<IReadOnlyList<StoredPackage>> GetByStateAllTenantsAsync(PackageLoadState state, CancellationToken ct = default)
    {
        var status = state.ToString();
        var records = await _db.KnowledgePackages.AsNoTracking()
            .Where(p => p.Status == status)
            .ToListAsync(ct);
        return records.Select(ToStoredPackage).ToList();
    }

    public async Task<IReadOnlyList<StoredPackage>> GetAllAcrossTenantsAsync(CancellationToken ct = default)
    {
        var records = await _db.KnowledgePackages.AsNoTracking().ToListAsync(ct);
        return records.Select(ToStoredPackage).ToList();
    }

    private static StoredPackage ToStoredPackage(KnowledgePackageRecord record)
    {
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(record.ManifestJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Stored manifest for package '{record.PackageId}' version '{record.PackageVersion}' could not be deserialized.");

        var state = Enum.Parse<PackageLoadState>(record.Status);
        return new StoredPackage(record.TenantId, manifest, state, record.StatusDetail);
    }
}
