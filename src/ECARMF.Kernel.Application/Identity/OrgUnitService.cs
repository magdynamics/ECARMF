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

    /// <summary>Moves a unit to a new lifecycle state (Construction →
    /// Operating, Operating → Sold, ...). Audited, and generically raises a
    /// framework/package review notification — a stabilizing hotel and a
    /// closing dental practice use the exact same mechanism. When the unit
    /// carries a lifecycle-package map, mapped packages follow the state
    /// automatically (Rosetta Requirement 3).</summary>
    Task<OrganizationalUnit> SetLifecycleStateAsync(
        string tenantId, string unitId, string lifecycleState, string actor, CancellationToken ct = default);

    /// <summary>Declares which packages belong to which lifecycle state for
    /// this unit — configuration, not code: a Project can state that
    /// Construction runs the build-chain package and OperatingAsset runs the
    /// real-estate package, and the swap happens on state change.</summary>
    Task<OrganizationalUnit> SetLifecyclePackageMapAsync(
        string tenantId, string unitId, Dictionary<string, List<string>> map, string actor,
        CancellationToken ct = default);
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
    private readonly Workflow.INotificationStore? _notifications;

    public OrgUnitService(
        IOrgUnitStore units, IPackageStore packages, IAuditLog audit,
        Workflow.INotificationStore? notifications = null)
    {
        _units = units;
        _packages = packages;
        _audit = audit;
        _notifications = notifications;
    }

    public async Task<OrganizationalUnit> SetLifecycleStateAsync(
        string tenantId, string unitId, string lifecycleState, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lifecycleState))
        {
            throw new ArgumentException("lifecycleState is required (e.g. Construction, Operating, Sold).");
        }

        var unit = await _units.GetAsync(tenantId, unitId, ct)
            ?? throw new KeyNotFoundException($"Unit '{unitId}' does not exist.");

        var previous = unit.LifecycleState;
        if (string.Equals(previous, lifecycleState.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return unit;
        }

        unit.LifecycleState = lifecycleState.Trim();

        // Lifecycle-aware framework attachment (Rosetta Requirement 3):
        // packages mapped to the NEW state attach; packages mapped
        // exclusively to OTHER states detach. Hand-attached packages
        // (absent from the map entirely) are never touched.
        var attached = new List<string>();
        var detached = new List<string>();
        if (unit.LifecyclePackageMap.Count > 0)
        {
            var forNewState = unit.LifecyclePackageMap.TryGetValue(unit.LifecycleState, out var mapped)
                ? mapped : [];
            var allMapped = unit.LifecyclePackageMap.Values
                .SelectMany(p => p).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var packageId in allMapped)
            {
                var belongsNow = forNewState.Contains(packageId, StringComparer.OrdinalIgnoreCase);
                var isAttached = unit.AttachedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
                if (belongsNow && !isAttached)
                {
                    unit.AttachedPackageIds.Add(packageId);
                    attached.Add(packageId);
                }
                else if (!belongsNow && isAttached)
                {
                    unit.AttachedPackageIds.RemoveAll(p =>
                        string.Equals(p, packageId, StringComparison.OrdinalIgnoreCase));
                    detached.Add(packageId);
                }
            }
        }

        unit.UpdatedAt = DateTimeOffset.UtcNow;
        await _units.UpdateAsync(unit, ct);

        await AuditAsync(tenantId, actor, AuditCategories.OrgUnitChanged,
            $"Unit '{unit.Name}' lifecycle changed: {previous} → {unit.LifecycleState}.", unit, ct);
        if (attached.Count > 0 || detached.Count > 0)
        {
            await AuditAsync(tenantId, "system:flywheel", AuditCategories.OrgUnitPackagesChanged,
                $"Lifecycle-driven package switch on '{unit.Name}': " +
                (attached.Count > 0 ? $"attached {string.Join(", ", attached)}" : "") +
                (attached.Count > 0 && detached.Count > 0 ? "; " : "") +
                (detached.Count > 0 ? $"detached {string.Join(", ", detached)}" : "") +
                $" (state {previous} → {unit.LifecycleState}, per the unit's lifecycle-package map).",
                unit, ct);
        }

        // The generic reaction: a lifecycle change means the attached
        // frameworks/packages may no longer fit — surface a review, for ANY
        // unit type, never special-cased.
        if (_notifications is not null)
        {
            var switched = attached.Count > 0 || detached.Count > 0
                ? $" Lifecycle map applied automatically: attached [{string.Join(", ", attached)}], detached [{string.Join(", ", detached)}]."
                : string.Empty;
            await _notifications.AddAsync(new Domain.Workflow.NotificationItem
            {
                TenantId = tenantId,
                WorkflowId = $"org-unit:{unit.UnitId}",
                Target = "ExecutiveOwner",
                Message = $"'{unit.Name}' moved from {previous} to {unit.LifecycleState} — review its attached " +
                          $"packages and frameworks ({(unit.AttachedPackageIds.Count == 0 ? "none attached" : string.Join(", ", unit.AttachedPackageIds))}); " +
                          "a unit's intelligence should match its lifecycle stage." + switched,
                Severity = "Info",
                CorrelationId = Guid.NewGuid()
            }, ct);
        }

        return unit;
    }

    public async Task<OrganizationalUnit> SetLifecyclePackageMapAsync(
        string tenantId, string unitId, Dictionary<string, List<string>> map, string actor,
        CancellationToken ct = default)
    {
        var unit = await _units.GetAsync(tenantId, unitId, ct)
            ?? throw new KeyNotFoundException($"Unit '{unitId}' does not exist.");

        // Every mapped package must be Active for the tenant — a map
        // pointing at nothing would silently do nothing at transition time.
        var active = await _packages.GetByStateAsync(tenantId, PackageLoadState.Active, ct);
        var activeIds = active.Select(p => p.Manifest.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = map.Values.SelectMany(p => p)
            .Where(p => !activeIds.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"These packages are not Active for this tenant: {string.Join(", ", unknown)}. Activate them first.");
        }

        unit.LifecyclePackageMap = new Dictionary<string, List<string>>(map, StringComparer.OrdinalIgnoreCase);
        unit.UpdatedAt = DateTimeOffset.UtcNow;
        await _units.UpdateAsync(unit, ct);

        await AuditAsync(tenantId, actor, AuditCategories.OrgUnitPackagesChanged,
            $"Lifecycle-package map set on '{unit.Name}': " +
            string.Join("; ", map.Select(kv => $"{kv.Key} → [{string.Join(", ", kv.Value)}]")) + ".",
            unit, ct);

        return unit;
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
