using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The package catalog is what turns tenant-authored packages into a
/// platform library: cloning must isolate tenants, dependencies must activate
/// in order, and re-installs must be no-ops.</summary>
public class PackageCatalogTests
{
    private static (PackageCatalogService Service, InMemoryPackageStore Store, StubPackageLoader Loader) Build()
    {
        var store = new InMemoryPackageStore();
        var loader = new StubPackageLoader(store);
        return (new PackageCatalogService(store, loader, new InMemoryAuditLog()), store, loader);
    }

    private static KnowledgePackageManifest Manifest(
        string id, string version = "1.0.0", params string[] dependsOn) => new()
    {
        PackageId = id,
        Name = id,
        PackageVersion = version,
        Publisher = "Test",
        EntityId = Guid.NewGuid(),
        TenantId = "source",
        Dependencies = dependsOn.Select(d => new PackageDependency { PackageId = d }).ToList()
    };

    [Fact]
    public async Task List_dedupes_across_tenants_and_reports_only_active_installs()
    {
        var (service, store, _) = Build();
        await store.AddAsync("a", Manifest("ecarmf.p1"), PackageLoadState.Active, null);
        await store.AddAsync("b", Manifest("ecarmf.p1"), PackageLoadState.Active, null);
        await store.AddAsync("c", Manifest("ecarmf.p1"), PackageLoadState.Deactivated, null);

        var entries = await service.ListAsync();

        var p1 = Assert.Single(entries, e => e.PackageId == "ecarmf.p1");
        Assert.Equal(2, p1.InstalledInTenants.Count);            // a + b only
        Assert.DoesNotContain("c", p1.InstalledInTenants);       // deactivated ≠ installed
    }

    [Fact]
    public async Task Install_clones_the_manifest_so_tenants_never_share_an_entity_row()
    {
        var (service, store, _) = Build();
        var source = Manifest("ecarmf.p1");
        var sourceEntity = source.EntityId;
        await store.AddAsync("source", source, PackageLoadState.Active, null);

        var result = await service.InstallAsync("ecarmf.p1", "1.0.0", "target", "op", withDependencies: false);

        Assert.Empty(result.Errors);
        Assert.Single(result.Activated);
        // Source untouched; target copy is a NEW entity bound to the target tenant.
        Assert.Equal(sourceEntity, source.EntityId);
        Assert.Equal("source", source.TenantId);
        var copy = (await store.GetAsync("target", "ecarmf.p1", "1.0.0"))!;
        Assert.NotEqual(sourceEntity, copy.Manifest.EntityId);
        Assert.Equal("target", copy.Manifest.TenantId);
        Assert.Equal(PackageLoadState.Active, copy.State);
    }

    [Fact]
    public async Task Install_resolves_dependencies_and_activates_them_in_topological_order()
    {
        var (service, store, loader) = Build();
        await store.AddAsync("source", Manifest("ecarmf.foundation"), PackageLoadState.Active, null);
        await store.AddAsync("source", Manifest("ecarmf.feature", "1.0.0", "ecarmf.foundation"), PackageLoadState.Active, null);

        var result = await service.InstallAsync("ecarmf.feature", "1.0.0", "target", "op", withDependencies: true);

        Assert.Equal(2, result.Activated.Count);
        // Foundation must activate BEFORE the feature that depends on it.
        Assert.Equal("ecarmf.foundation@1.0.0", loader.Activated[0]);
        Assert.Equal("ecarmf.feature@1.0.0", loader.Activated[1]);
    }

    [Fact]
    public async Task Install_is_idempotent_for_packages_the_tenant_already_has()
    {
        var (service, store, loader) = Build();
        await store.AddAsync("source", Manifest("ecarmf.p1"), PackageLoadState.Active, null);
        await store.AddAsync("target", Manifest("ecarmf.p1"), PackageLoadState.Active, null);

        var result = await service.InstallAsync("ecarmf.p1", "1.0.0", "target", "op", withDependencies: true);

        Assert.Empty(result.Activated);
        Assert.Single(result.Skipped);
        Assert.Empty(loader.Loaded);
    }

    [Fact]
    public async Task Install_surfaces_activation_failures_without_throwing()
    {
        var (service, store, loader) = Build();
        await store.AddAsync("source", Manifest("ecarmf.p1"), PackageLoadState.Active, null);
        loader.FailActivation.Add("ecarmf.p1");

        var result = await service.InstallAsync("ecarmf.p1", "1.0.0", "target", "op", withDependencies: false);

        Assert.Empty(result.Activated);
        Assert.Contains(result.Errors, e => e.Contains("activation failed"));
    }

    [Fact]
    public async Task Install_of_an_unknown_package_reports_a_clear_error()
    {
        var (service, _, _) = Build();
        var result = await service.InstallAsync("ecarmf.ghost", "9.9.9", "target", "op", true);
        Assert.Contains(result.Errors, e => e.Contains("not in the catalog"));
    }
}
