using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class PackageLoaderTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryPackageStore _store = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InMemoryAuditLog _audit = new();

    private PackageLoader CreateLoader() => new(_store, _registries, _audit);

    private TenantRegistries Registries(string tenantId = Tenant) => _registries.GetFor(tenantId);

    private static KnowledgePackageManifest ValidManifest(
        string packageId = "pkg.test", string version = "1.0.0") => new()
    {
        PackageId = packageId,
        Name = "Test Package",
        PackageVersion = version,
        Publisher = "Tests",
        Events = [new EventDeclaration { EventName = "TransactionReceived" }],
        Rules =
        [
            new RuleDeclaration
            {
                RuleId = $"{packageId}.R-1",
                Name = "Test rule",
                TriggerEvent = "TransactionReceived",
                Priority = 100,
                Conditions = [new RuleCondition { Field = "amount", Operator = ConditionOperator.GreaterThan, Value = "10" }],
                OutcomeOnMatch = RuleOutcome.Flagged,
                ReasonTemplate = "amount {amount} too high"
            }
        ],
        Entities = [new EntityDeclaration { EntityTypeName = $"{packageId}.Entity" }],
        Capabilities = [new CapabilityDeclaration { CapabilityId = $"{packageId}.Cap" }]
    };

    [Fact]
    public async Task Load_valid_manifest_stages_it()
    {
        var loader = CreateLoader();

        var result = await loader.LoadAsync(Tenant, ValidManifest());

        Assert.True(result.Success);
        Assert.Equal(PackageLoadState.Staged, result.State);
        Assert.Equal(PackageLoadState.Staged, _store.Items.Single().State);
        // Staging must not touch the registries.
        Assert.Empty(Registries().Rules.GetAll());
    }

    [Fact]
    public async Task Load_malformed_manifest_fails_with_full_error_list()
    {
        var loader = CreateLoader();
        var manifest = ValidManifest();
        manifest.Name = "";
        manifest.PackageVersion = "not-a-version";
        manifest.Rules[0].TriggerEvent = "UndeclaredEvent";
        manifest.Rules[0].Conditions[0].Field = "";

        var result = await loader.LoadAsync(Tenant, manifest);

        Assert.False(result.Success);
        Assert.Equal(PackageLoadState.Failed, result.State);
        Assert.Contains(result.Errors, e => e.Contains("Name is required"));
        Assert.Contains(result.Errors, e => e.Contains("not a valid version"));
        Assert.Contains(result.Errors, e => e.Contains("UndeclaredEvent"));
        Assert.Contains(result.Errors, e => e.Contains("no Field"));
        // The failed attempt is persisted and explainable.
        Assert.Equal(PackageLoadState.Failed, _store.Items.Single().State);
        Assert.NotNull(_store.Items.Single().StatusDetail);
    }

    [Fact]
    public async Task Load_duplicate_package_version_is_rejected()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, ValidManifest());

        var result = await loader.LoadAsync(Tenant, ValidManifest());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("already loaded"));
    }

    [Fact]
    public async Task Same_package_can_be_loaded_by_two_tenants_independently()
    {
        var loader = CreateLoader();

        var first = await loader.LoadAsync("tenant-a", ValidManifest());
        var second = await loader.LoadAsync("tenant-b", ValidManifest());

        Assert.True(first.Success);
        Assert.True(second.Success);
    }

    [Fact]
    public async Task Activate_happy_path_registers_all_declarations()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, ValidManifest());

        var result = await loader.ActivateAsync(Tenant, "pkg.test", "1.0.0");

        Assert.True(result.Success);
        Assert.Equal(PackageLoadState.Active, result.State);
        Assert.True(Registries().Events.IsDeclared("TransactionReceived"));
        Assert.True(Registries().Rules.Contains("pkg.test.R-1"));
        Assert.True(Registries().Entities.Contains("pkg.test.Entity"));
        Assert.True(Registries().Capabilities.Contains("pkg.test.Cap"));
    }

    [Fact]
    public async Task Activation_is_isolated_per_tenant()
    {
        var loader = CreateLoader();
        await loader.LoadAsync("tenant-a", ValidManifest());
        await loader.ActivateAsync("tenant-a", "pkg.test", "1.0.0");

        // Tenant B sees nothing from tenant A's activation.
        Assert.False(Registries("tenant-b").Rules.Contains("pkg.test.R-1"));
        Assert.False(Registries("tenant-b").Events.IsDeclared("TransactionReceived"));

        // And tenant B can activate the same package without conflict.
        await loader.LoadAsync("tenant-b", ValidManifest());
        var result = await loader.ActivateAsync("tenant-b", "pkg.test", "1.0.0");
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Activate_with_missing_dependency_fails_and_registers_nothing()
    {
        var loader = CreateLoader();
        var manifest = ValidManifest();
        manifest.Dependencies = [new PackageDependency { PackageId = "pkg.base", MinimumVersion = "1.0.0" }];
        await loader.LoadAsync(Tenant, manifest);

        var result = await loader.ActivateAsync(Tenant, "pkg.test", "1.0.0");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("pkg.base") && e.Contains("not active"));
        Assert.Empty(Registries().Rules.GetAll());
        // State is unchanged so the dependency can be activated and retried.
        Assert.Equal(PackageLoadState.Staged, _store.Items.Single().State);
    }

    [Fact]
    public async Task Activate_with_satisfied_dependency_succeeds()
    {
        var loader = CreateLoader();
        var baseManifest = ValidManifest("pkg.base");
        await loader.LoadAsync(Tenant, baseManifest);
        await loader.ActivateAsync(Tenant, "pkg.base", "1.0.0");

        var dependent = ValidManifest("pkg.dependent");
        dependent.Events = []; // TransactionReceived comes from pkg.base
        dependent.Dependencies = [new PackageDependency { PackageId = "pkg.base", MinimumVersion = "1.0.0" }];
        await loader.LoadAsync(Tenant, dependent);

        var result = await loader.ActivateAsync(Tenant, "pkg.dependent", "1.0.0");

        Assert.True(result.Success);
        Assert.Equal(PackageLoadState.Active, result.State);
    }

    [Fact]
    public async Task Dependency_active_for_another_tenant_does_not_satisfy()
    {
        var loader = CreateLoader();
        await loader.LoadAsync("tenant-b", ValidManifest("pkg.base"));
        await loader.ActivateAsync("tenant-b", "pkg.base", "1.0.0");

        var dependent = ValidManifest("pkg.dependent");
        dependent.Dependencies = [new PackageDependency { PackageId = "pkg.base", MinimumVersion = "1.0.0" }];
        await loader.LoadAsync("tenant-a", dependent);

        var result = await loader.ActivateAsync("tenant-a", "pkg.dependent", "1.0.0");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("pkg.base") && e.Contains("not active"));
    }

    [Fact]
    public async Task Activate_registry_conflict_rolls_back_and_marks_failed()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, ValidManifest("pkg.a"));
        await loader.ActivateAsync(Tenant, "pkg.a", "1.0.0");

        // pkg.b declares the same event name TransactionReceived -> conflict.
        var conflicting = ValidManifest("pkg.b");
        await loader.LoadAsync(Tenant, conflicting);
        var result = await loader.ActivateAsync(Tenant, "pkg.b", "1.0.0");

        Assert.False(result.Success);
        Assert.Equal(PackageLoadState.Failed, result.State);
        // pkg.b contributions rolled back; pkg.a untouched.
        Assert.False(Registries().Rules.Contains("pkg.b.R-1"));
        Assert.True(Registries().Rules.Contains("pkg.a.R-1"));
    }

    [Fact]
    public async Task Deactivate_refused_while_active_dependent_exists()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, ValidManifest("pkg.base"));
        await loader.ActivateAsync(Tenant, "pkg.base", "1.0.0");

        var dependent = ValidManifest("pkg.dependent");
        dependent.Events = [];
        dependent.Dependencies = [new PackageDependency { PackageId = "pkg.base" }];
        await loader.LoadAsync(Tenant, dependent);
        await loader.ActivateAsync(Tenant, "pkg.dependent", "1.0.0");

        var result = await loader.DeactivateAsync(Tenant, "pkg.base", "1.0.0");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("pkg.dependent"));
    }

    [Fact]
    public async Task Deactivate_withdraws_declarations()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, ValidManifest());
        await loader.ActivateAsync(Tenant, "pkg.test", "1.0.0");

        var result = await loader.DeactivateAsync(Tenant, "pkg.test", "1.0.0");

        Assert.True(result.Success);
        Assert.Equal(PackageLoadState.Deactivated, result.State);
        Assert.Empty(Registries().Rules.GetAll());
        Assert.False(Registries().Events.IsDeclared("TransactionReceived"));
    }

    [Fact]
    public async Task Rehydrate_restores_active_packages_per_tenant()
    {
        var loader = CreateLoader();
        await loader.LoadAsync("tenant-a", ValidManifest());
        await loader.ActivateAsync("tenant-a", "pkg.test", "1.0.0");
        await loader.LoadAsync("tenant-b", ValidManifest("pkg.other"));
        await loader.ActivateAsync("tenant-b", "pkg.other", "1.0.0");

        // Simulate restart: fresh registries, same store.
        var freshProvider = new TenantRegistryProvider();
        var rebooted = new PackageLoader(_store, freshProvider, _audit);

        await rebooted.RehydrateActiveAsync();

        Assert.True(freshProvider.GetFor("tenant-a").Rules.Contains("pkg.test.R-1"));
        Assert.False(freshProvider.GetFor("tenant-a").Rules.Contains("pkg.other.R-1"));
        Assert.True(freshProvider.GetFor("tenant-b").Rules.Contains("pkg.other.R-1"));
        Assert.False(freshProvider.GetFor("tenant-b").Rules.Contains("pkg.test.R-1"));
    }
}
