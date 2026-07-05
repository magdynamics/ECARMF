using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Application.Identity;

/// <summary>Tenant-scoped persistence for the organizational hierarchy.</summary>
public interface IOrgUnitStore
{
    Task<OrganizationalUnit?> GetAsync(string tenantId, string unitId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationalUnit>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(OrganizationalUnit unit, CancellationToken ct = default);
    Task UpdateAsync(OrganizationalUnit unit, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default);
}

public interface IOrgUnitService
{
    Task<OrganizationalUnit> CreateAsync(
        string tenantId, string unitId, string name, string unitType,
        string? parentUnitId, string? industry, string? notes, string actor,
        CancellationToken ct = default);

    Task<OrganizationalUnit> UpdateAsync(
        string tenantId, string unitId, string name, string unitType,
        string? parentUnitId, string? industry, string? notes, string actor,
        CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default);

    Task<OrganizationalUnit> AttachPackageAsync(
        string tenantId, string unitId, string packageId, string actor, CancellationToken ct = default);

    Task<OrganizationalUnit> DetachPackageAsync(
        string tenantId, string unitId, string packageId, string actor, CancellationToken ct = default);
}

/// <summary>
/// The hierarchy's invariants live here, not in the endpoint: a parent must
/// exist in the same tenant, reparenting can never create a cycle, a unit
/// with children cannot be deleted, and only the tenant's Active packages
/// can attach to a unit. Attachment changes are audited — which
/// intelligence runs where is a controlled decision, not a UI convenience.
/// </summary>
public class OrgUnitService : IOrgUnitService
{
    private readonly IOrgUnitStore _units;
    private readonly IPackageStore _packages;
    private readonly IAuditLog _audit;

    public OrgUnitService(IOrgUnitStore units, IPackageStore packages, IAuditLog audit)
    {
        _units = units;
        _packages = packages;
        _audit = audit;
    }

    public async Task<OrganizationalUnit> CreateAsync(
        string tenantId, string unitId, string name, string unitType,
        string? parentUnitId, string? industry, string? notes, string actor,
        CancellationToken ct = default)
    {
        if (await _units.GetAsync(tenantId, unitId, ct) is not null)
        {
            throw new ArgumentException($"Unit '{unitId}' already exists.");
        }

        await RequireValidParentAsync(tenantId, unitId, parentUnitId, ct);

        var unit = new OrganizationalUnit
        {
            TenantId = tenantId,
            UnitId = unitId,
            Name = name,
            UnitType = unitType,
            ParentUnitId = parentUnitId,
            Industry = industry,
            Notes = notes,
            CreatedBy = actor
        };
        await _units.AddAsync(unit, ct);
        await AuditAsync(tenantId, actor, AuditCategories.OrgUnitChanged,
            $"Unit '{name}' ({unitType}) created" + (parentUnitId is null ? " at root." : $" under '{parentUnitId}'."),
            unit, ct);
        return unit;
    }

    public async Task<OrganizationalUnit> UpdateAsync(
        string tenantId, string unitId, string name, string unitType,
        string? parentUnitId, string? industry, string? notes, string actor,
        CancellationToken ct = default)
    {
        var unit = await _units.GetAsync(tenantId, unitId, ct)
            ?? throw new KeyNotFoundException($"Unit '{unitId}' does not exist.");

        await RequireValidParentAsync(tenantId, unitId, parentUnitId, ct);

        unit.Name = name;
        unit.UnitType = unitType;
        unit.ParentUnitId = parentUnitId;
        unit.Industry = industry;
        unit.Notes = notes;
        unit.UpdatedAt = DateTimeOffset.UtcNow;
        await _units.UpdateAsync(unit, ct);
        return unit;
    }

    public async Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default)
    {
        var all = await _units.GetAllAsync(tenantId, ct);
        if (all.Any(u => string.Equals(u.ParentUnitId, unitId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Unit '{unitId}' has child units — move or delete them first.");
        }

        await _units.DeleteAsync(tenantId, unitId, ct);
    }

    public async Task<OrganizationalUnit> AttachPackageAsync(
        string tenantId, string unitId, string packageId, string actor, CancellationToken ct = default)
    {
        var unit = await _units.GetAsync(tenantId, unitId, ct)
            ?? throw new KeyNotFoundException($"Unit '{unitId}' does not exist.");

        var active = await _packages.GetByStateAsync(tenantId, PackageLoadState.Active, ct);
        if (!active.Any(p => string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Package '{packageId}' is not Active for this tenant — activate it before attaching.");
        }

        if (!unit.AttachedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
        {
            unit.AttachedPackageIds.Add(packageId);
            unit.UpdatedAt = DateTimeOffset.UtcNow;
            await _units.UpdateAsync(unit, ct);
            await AuditAsync(tenantId, actor, AuditCategories.OrgUnitPackagesChanged,
                $"Package '{packageId}' attached to unit '{unit.Name}'.", unit, ct);
        }

        return unit;
    }

    public async Task<OrganizationalUnit> DetachPackageAsync(
        string tenantId, string unitId, string packageId, string actor, CancellationToken ct = default)
    {
        var unit = await _units.GetAsync(tenantId, unitId, ct)
            ?? throw new KeyNotFoundException($"Unit '{unitId}' does not exist.");

        if (unit.AttachedPackageIds.RemoveAll(p =>
                string.Equals(p, packageId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            unit.UpdatedAt = DateTimeOffset.UtcNow;
            await _units.UpdateAsync(unit, ct);
            await AuditAsync(tenantId, actor, AuditCategories.OrgUnitPackagesChanged,
                $"Package '{packageId}' detached from unit '{unit.Name}'.", unit, ct);
        }

        return unit;
    }

    /// <summary>The parent must exist in the same tenant, and following its
    /// ancestry must never arrive back at the unit being placed (no cycles).</summary>
    private async Task RequireValidParentAsync(
        string tenantId, string unitId, string? parentUnitId, CancellationToken ct)
    {
        if (parentUnitId is null)
        {
            return;
        }

        if (string.Equals(parentUnitId, unitId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A unit cannot be its own parent.");
        }

        var all = (await _units.GetAllAsync(tenantId, ct))
            .ToDictionary(u => u.UnitId, StringComparer.OrdinalIgnoreCase);
        if (!all.ContainsKey(parentUnitId))
        {
            throw new ArgumentException($"Parent unit '{parentUnitId}' does not exist in this tenant.");
        }

        var cursor = parentUnitId;
        var hops = 0;
        while (cursor is not null && hops++ < 100)
        {
            if (string.Equals(cursor, unitId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Placing the unit under '{parentUnitId}' would create a cycle.");
            }

            cursor = all.TryGetValue(cursor, out var node) ? node.ParentUnitId : null;
        }
    }

    private Task AuditAsync(
        string tenantId, string actor, string category, string summary,
        OrganizationalUnit unit, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid(),
            Category = category,
            Actor = actor,
            Summary = summary,
            Detail = new Dictionary<string, string>
            {
                ["unitId"] = unit.UnitId,
                ["unitType"] = unit.UnitType,
                ["parentUnitId"] = unit.ParentUnitId ?? "",
                ["attachedPackages"] = string.Join(", ", unit.AttachedPackageIds)
            }
        }, ct);
}
