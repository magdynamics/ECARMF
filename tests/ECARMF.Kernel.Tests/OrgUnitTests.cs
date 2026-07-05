using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryOrgUnitStore : IOrgUnitStore
{
    public List<OrganizationalUnit> Items { get; } = [];

    public Task<OrganizationalUnit?> GetAsync(string tenantId, string unitId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(u =>
            u.TenantId == tenantId && string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<OrganizationalUnit>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrganizationalUnit>>(Items.Where(u => u.TenantId == tenantId).ToList());

    public Task AddAsync(OrganizationalUnit unit, CancellationToken ct = default)
    { Items.Add(unit); return Task.CompletedTask; }

    public Task UpdateAsync(OrganizationalUnit unit, CancellationToken ct = default)
    {
        var index = Items.FindIndex(u => u.TenantId == unit.TenantId && u.UnitId == unit.UnitId);
        if (index >= 0) Items[index] = unit;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, string unitId, CancellationToken ct = default)
    { Items.RemoveAll(u => u.TenantId == tenantId && u.UnitId == unitId); return Task.CompletedTask; }
}

/// <summary>The tenant's shape is data: any hierarchy, no cycles, packages
/// attach per unit and only when Active for that tenant.</summary>
public class OrgUnitTests
{
    private const string Tenant = "dental-group";

    private readonly InMemoryOrgUnitStore _units = new();
    private readonly InMemoryPackageStore _packages = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly OrgUnitService _service;

    public OrgUnitTests()
    {
        _service = new OrgUnitService(_units, _packages, _audit);
    }

    private Task<OrganizationalUnit> Create(string unitId, string? parent = null, string type = "Location") =>
        _service.CreateAsync(Tenant, unitId, unitId, type, parent, null, null, "admin");

    [Fact]
    public async Task Any_shape_and_depth_builds_from_data()
    {
        await Create("midwest", type: "Division");
        await Create("dental-llc", "midwest", type: "LegalEntity");
        await Create("orland-park", "dental-llc", type: "Location");
        await Create("orland-park-building", "orland-park", type: "Property");

        var all = await _units.GetAllAsync(Tenant);
        Assert.Equal(4, all.Count);
        Assert.Equal("orland-park", all.Single(u => u.UnitId == "orland-park-building").ParentUnitId);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.OrgUnitChanged);
    }

    [Fact]
    public async Task Parent_must_exist_and_cycles_are_impossible()
    {
        await Create("a");
        await Create("b", "a");
        await Create("c", "b");

        await Assert.ThrowsAsync<ArgumentException>(() => Create("orphan", "nope"));
        // Reparenting a under c would make a → c → b → a.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(Tenant, "a", "a", "Location", "c", null, null, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(Tenant, "a", "a", "Location", "a", null, null, "admin"));
    }

    [Fact]
    public async Task A_unit_with_children_cannot_be_deleted()
    {
        await Create("region", type: "Region");
        await Create("clinic", "region");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(Tenant, "region"));
        await _service.DeleteAsync(Tenant, "clinic"); // leaf deletes fine
        await _service.DeleteAsync(Tenant, "region"); // then the parent can go
        Assert.Empty(_units.Items);
    }

    [Fact]
    public async Task Only_the_tenants_active_packages_attach()
    {
        await Create("clinic");
        await _packages.AddAsync(Tenant, new KnowledgePackageManifest
        {
            PackageId = "ai-dental", Name = "AI Dental", PackageVersion = "1.0.0"
        }, PackageLoadState.Active, null);
        await _packages.AddAsync(Tenant, new KnowledgePackageManifest
        {
            PackageId = "ai-banking", Name = "AI Banking", PackageVersion = "1.0.0"
        }, PackageLoadState.Staged, null); // staged, not active

        var unit = await _service.AttachPackageAsync(Tenant, "clinic", "ai-dental", "admin");
        Assert.Contains("ai-dental", unit.AttachedPackageIds);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.OrgUnitPackagesChanged);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AttachPackageAsync(Tenant, "clinic", "ai-banking", "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AttachPackageAsync(Tenant, "clinic", "never-loaded", "admin"));

        // Attach is idempotent; detach removes.
        await _service.AttachPackageAsync(Tenant, "clinic", "ai-dental", "admin");
        Assert.Single((await _units.GetAsync(Tenant, "clinic"))!.AttachedPackageIds);
        var detached = await _service.DetachPackageAsync(Tenant, "clinic", "ai-dental", "admin");
        Assert.Empty(detached.AttachedPackageIds);
    }
}
