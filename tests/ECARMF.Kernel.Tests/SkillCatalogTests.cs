using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>Skills are the commercial layer: tier/packaging decide what a
/// client is billed, so classification, override precedence, and the
/// billed-set rules must be exact.</summary>
public class SkillCatalogTests
{
    private const string Tenant = "t1";

    private static (SkillCatalogService Service, StubPackageCatalog Catalog,
        InMemoryPackageStore Store, StubPackageLoader Loader, InMemorySkillSettingStore Settings) Build()
    {
        var catalog = new StubPackageCatalog();
        var store = new InMemoryPackageStore();
        var loader = new StubPackageLoader(store);
        var settings = new InMemorySkillSettingStore();
        return (new SkillCatalogService(catalog, store, loader, settings), catalog, store, loader, settings);
    }

    private static KnowledgePackageManifest Manifest(string id, string name, string version = "1.0.0") =>
        new() { PackageId = id, Name = name, PackageVersion = version, Publisher = "Test" };

    [Theory]
    [InlineData("ecarmf.ai-tcel-foundation", "TCEL Enterprise Foundation", SkillTiers.Core, 0)]
    [InlineData("ecarmf.x", "Bank Integration Pack", SkillTiers.Core, 0)]
    [InlineData("ecarmf.x", "Renewal Calendar", SkillTiers.Core, 0)]
    [InlineData("ecarmf.ai-autonomous-orchestration", "Autonomous Orchestration", SkillTiers.AddOn, 1500)]
    [InlineData("ecarmf.ai-financial-continuity", "Financial Continuity & Liquidity", SkillTiers.AddOn, 1500)]
    [InlineData("ecarmf.ai-dental", "AI Dental", SkillTiers.Industry, 500)]
    public void Classify_assigns_tier_and_default_price(string id, string name, string tier, int price)
    {
        var (t, p) = SkillCatalogService.Classify(id, name);
        Assert.Equal(tier, t);
        Assert.Equal(price, p);
    }

    [Fact]
    public async Task Stored_packaging_override_beats_the_code_default()
    {
        var (service, catalog, _, _, settings) = Build();
        catalog.Entries.Add(StubPackageCatalog.Entry("ecarmf.ai-dental", "AI Dental"));
        await settings.UpsertAsync(new SkillSetting("ecarmf.ai-dental", SkillPackaging.AlaCarte, 900m), "op");

        var skills = await service.ListForTenantAsync(Tenant);

        var dental = skills.Single(s => s.PackageId == "ecarmf.ai-dental");
        Assert.Equal(SkillPackaging.AlaCarte, dental.Packaging);
        Assert.Equal(900m, dental.MonthlyPrice);
    }

    [Fact]
    public async Task Default_packaging_is_alacarte_for_addons_and_essential_for_the_rest()
    {
        var (service, catalog, _, _, _) = Build();
        catalog.Entries.Add(StubPackageCatalog.Entry("ecarmf.ai-autonomous-orchestration", "Autonomous Orchestration"));
        catalog.Entries.Add(StubPackageCatalog.Entry("ecarmf.ai-dental", "AI Dental"));

        var skills = await service.ListForTenantAsync(Tenant);

        Assert.Equal(SkillPackaging.AlaCarte, skills.Single(s => s.PackageId.Contains("orchestration")).Packaging);
        var dental = skills.Single(s => s.PackageId == "ecarmf.ai-dental");
        Assert.Equal(SkillPackaging.Essential, dental.Packaging);
        Assert.Equal(0m, dental.MonthlyPrice); // essential is bundled, never billed
    }

    [Fact]
    public async Task SetPackaging_forces_essential_price_to_zero_and_rejects_invalid_input()
    {
        var (service, _, _, _, settings) = Build();

        var essential = await service.SetPackagingAsync("ecarmf.x", SkillPackaging.Essential, 750m, "op");
        Assert.True(essential.Success);
        Assert.Equal(0m, (await settings.GetAllAsync())["ecarmf.x"].MonthlyPrice);

        Assert.False((await service.SetPackagingAsync("ecarmf.x", "Bundled", 1m, "op")).Success);
        Assert.False((await service.SetPackagingAsync("ecarmf.x", SkillPackaging.AlaCarte, -5m, "op")).Success);
    }

    [Fact]
    public async Task Billing_charges_only_active_alacarte_skills()
    {
        var (service, _, store, _, settings) = Build();
        await store.AddAsync(Tenant, Manifest("ecarmf.ai-autonomous-orchestration", "Autonomous Orchestration"), PackageLoadState.Active, null);
        await store.AddAsync(Tenant, Manifest("ecarmf.ai-dental", "AI Dental"), PackageLoadState.Active, null);              // essential by default
        await store.AddAsync(Tenant, Manifest("ecarmf.ai-financial-continuity", "Financial Continuity"), PackageLoadState.Deactivated, null); // priced but OFF
        await settings.UpsertAsync(new SkillSetting("ecarmf.ai-dental", SkillPackaging.AlaCarte, 300m), "op");

        var priced = await service.ActivePricedSkillsAsync(Tenant);

        Assert.Equal(2, priced.Count); // orchestration (default à la carte) + dental (override)
        Assert.Contains(priced, p => p.Price == 1500m);
        Assert.Contains(priced, p => p.Price == 300m);
        Assert.DoesNotContain(priced, p => p.Name.Contains("Continuity")); // inactive — never billed
    }

    [Fact]
    public async Task Activate_installs_from_the_library_when_absent_and_reactivates_when_stored()
    {
        var (service, catalog, store, loader, _) = Build();
        catalog.Entries.Add(StubPackageCatalog.Entry("ecarmf.ai-dental", "AI Dental"));

        // Absent → catalog install with dependencies.
        var installed = await service.ActivateAsync("ecarmf.ai-dental", Tenant, "op");
        Assert.True(installed.Success);
        Assert.Contains(catalog.Installs, i => i.PackageId == "ecarmf.ai-dental" && i.WithDeps);

        // Stored but deactivated → plain re-activation, no re-install.
        await store.AddAsync(Tenant, Manifest("ecarmf.ai-spa", "AI Spa"), PackageLoadState.Deactivated, null);
        var reactivated = await service.ActivateAsync("ecarmf.ai-spa", Tenant, "op");
        Assert.True(reactivated.Success);
        Assert.Contains("ecarmf.ai-spa@1.0.0", loader.Activated);
        Assert.DoesNotContain(catalog.Installs, i => i.PackageId == "ecarmf.ai-spa");
    }

    [Fact]
    public async Task Deactivate_turns_the_package_off_and_is_idempotent()
    {
        var (service, _, store, loader, _) = Build();
        await store.AddAsync(Tenant, Manifest("ecarmf.ai-dental", "AI Dental"), PackageLoadState.Active, null);

        Assert.True((await service.DeactivateAsync("ecarmf.ai-dental", Tenant, "op")).Success);
        Assert.Contains("ecarmf.ai-dental@1.0.0", loader.Deactivated);

        // Already off → success without another loader call.
        var again = await service.DeactivateAsync("ecarmf.ai-dental", Tenant, "op");
        Assert.True(again.Success);
        Assert.Single(loader.Deactivated);
    }
}
