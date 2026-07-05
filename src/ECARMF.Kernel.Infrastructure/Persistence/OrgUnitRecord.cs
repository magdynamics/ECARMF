using System.Text.Json;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class OrgUnitRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public string? ParentUnitId { get; set; }
    public string? Industry { get; set; }
    public string AttachedPackageIdsJson { get; set; } = "[]";
    public string? Notes { get; set; }
    public string Status { get; set; } = "Active";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfOrgUnitStore : IOrgUnitStore
{
    private readonly ECARMFDbContext _db;

    public EfOrgUnitStore(ECARMFDbContext db) => _db = db;

    public async Task<OrganizationalUnit?> GetAsync(string tenantId, string unitId, CancellationToken ct = default)
    {
        var record = await _db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UnitId == unitId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<OrganizationalUnit>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId).OrderBy(u => u.Name).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(OrganizationalUnit unit, CancellationToken ct = default)
    {
        _db.OrgUnits.Add(ToRecord(unit));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(OrganizationalUnit unit, CancellationToken ct = default)
    {
        var record = await _db.OrgUnits.FirstAsync(
            u => u.TenantId == unit.TenantId && u.UnitId == unit.UnitId, ct);
        record.Name = unit.Name;
        record.UnitType = unit.UnitType;
        record.ParentUnitId = unit.ParentUnitId;
        record.Industry = unit.Industry;
        record.AttachedPackageIdsJson = JsonSerializer.Serialize(unit.AttachedPackageIds);
        record.Notes = unit.Notes;
        record.Status = unit.Status;
        record.UpdatedAt = unit.UpdatedAt ?? DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default)
    {
        var record = await _db.OrgUnits.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.UnitId == unitId, ct);
        if (record is not null)
        {
            _db.OrgUnits.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static OrgUnitRecord ToRecord(OrganizationalUnit unit) => new()
    {
        Id = unit.Id,
        TenantId = unit.TenantId,
        UnitId = unit.UnitId,
        Name = unit.Name,
        UnitType = unit.UnitType,
        ParentUnitId = unit.ParentUnitId,
        Industry = unit.Industry,
        AttachedPackageIdsJson = JsonSerializer.Serialize(unit.AttachedPackageIds),
        Notes = unit.Notes,
        Status = unit.Status,
        CreatedBy = unit.CreatedBy,
        CreatedAt = unit.CreatedAt,
        UpdatedAt = unit.UpdatedAt
    };

    private static OrganizationalUnit ToDomain(OrgUnitRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        UnitId = record.UnitId,
        Name = record.Name,
        UnitType = record.UnitType,
        ParentUnitId = record.ParentUnitId,
        Industry = record.Industry,
        AttachedPackageIds = JsonSerializer.Deserialize<List<string>>(record.AttachedPackageIdsJson) ?? [],
        Notes = record.Notes,
        Status = record.Status,
        CreatedBy = record.CreatedBy,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}
