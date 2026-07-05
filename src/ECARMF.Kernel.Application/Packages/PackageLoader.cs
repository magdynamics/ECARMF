using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

public class PackageLoader : IPackageLoader
{
    private readonly IPackageStore _store;
    private readonly ITenantRegistryProvider _registries;
    private readonly IAuditLog _audit;

    public PackageLoader(
        IPackageStore store,
        ITenantRegistryProvider registries,
        IAuditLog audit)
    {
        _store = store;
        _registries = registries;
        _audit = audit;
    }

    public async Task<PackageOperationResult> LoadAsync(string tenantId, KnowledgePackageManifest manifest, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(manifest.PackageId)
            && !string.IsNullOrWhiteSpace(manifest.PackageVersion)
            && await _store.ExistsAsync(tenantId, manifest.PackageId, manifest.PackageVersion, ct))
        {
            return PackageOperationResult.Fail(null,
                $"Package '{manifest.PackageId}' version '{manifest.PackageVersion}' is already loaded.");
        }

        var registries = _registries.GetFor(tenantId);
        var errors = ManifestValidator.Validate(manifest, registries.Events);

        if (errors.Count > 0)
        {
            // Persist the failed attempt only when it is identifiable; an
            // unidentifiable manifest cannot satisfy the unique package index.
            if (!string.IsNullOrWhiteSpace(manifest.PackageId) && !string.IsNullOrWhiteSpace(manifest.PackageVersion))
            {
                await _store.AddAsync(tenantId, manifest, PackageLoadState.Failed, string.Join(" ", errors), ct);
                await AuditLifecycleAsync(tenantId, manifest, AuditCategories.PackageFailed,
                    $"Package '{manifest.PackageId}' v{manifest.PackageVersion} failed validation.", string.Join(" ", errors), ct);
            }

            return PackageOperationResult.Fail(PackageLoadState.Failed, errors);
        }

        await _store.AddAsync(tenantId, manifest, PackageLoadState.Staged, null, ct);
        await AuditLifecycleAsync(tenantId, manifest, AuditCategories.PackageLoaded,
            $"Package '{manifest.PackageId}' v{manifest.PackageVersion} staged.", null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Staged);
    }

    public async Task<PackageOperationResult> ActivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        var stored = await _store.GetAsync(tenantId, packageId, packageVersion, ct);
        if (stored is null)
        {
            return PackageOperationResult.Fail(null,
                $"Package '{packageId}' version '{packageVersion}' is not loaded.");
        }

        if (stored.State is not (PackageLoadState.Staged or PackageLoadState.Deactivated))
        {
            return PackageOperationResult.Fail(stored.State,
                $"Package '{packageId}' version '{packageVersion}' cannot be activated from state '{stored.State}'.");
        }

        var dependencyErrors = await ResolveDependenciesAsync(tenantId, stored.Manifest, ct);
        if (dependencyErrors.Count > 0)
        {
            return PackageOperationResult.Fail(stored.State, dependencyErrors);
        }

        var registries = _registries.GetFor(tenantId);

        try
        {
            RegisterAll(registries, stored.Manifest);
        }
        catch (RegistryConflictException ex)
        {
            UnregisterAll(registries, packageId, packageVersion);
            await _store.UpdateStateAsync(tenantId, packageId, packageVersion, PackageLoadState.Failed, ex.Message, ct);
            await AuditLifecycleAsync(tenantId, stored.Manifest, AuditCategories.PackageFailed,
                $"Package '{packageId}' v{packageVersion} activation failed.", ex.Message, ct);
            return PackageOperationResult.Fail(PackageLoadState.Failed, ex.Message);
        }

        await _store.UpdateStateAsync(tenantId, packageId, packageVersion, PackageLoadState.Active, null, ct);
        await AuditLifecycleAsync(tenantId, stored.Manifest, AuditCategories.PackageActivated,
            $"Package '{packageId}' v{packageVersion} activated.", null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Active);
    }

    public async Task<PackageOperationResult> DeactivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        var stored = await _store.GetAsync(tenantId, packageId, packageVersion, ct);
        if (stored is null)
        {
            return PackageOperationResult.Fail(null,
                $"Package '{packageId}' version '{packageVersion}' is not loaded.");
        }

        if (stored.State != PackageLoadState.Active)
        {
            return PackageOperationResult.Fail(stored.State,
                $"Package '{packageId}' version '{packageVersion}' cannot be deactivated from state '{stored.State}'.");
        }

        var actives = await _store.GetByStateAsync(tenantId, PackageLoadState.Active, ct);
        var dependent = actives.FirstOrDefault(p =>
            !string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && p.Manifest.Dependencies.Any(d =>
                string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase)));

        if (dependent is not null)
        {
            return PackageOperationResult.Fail(stored.State,
                $"Cannot deactivate: active package '{dependent.Manifest.PackageId}' version '{dependent.Manifest.PackageVersion}' depends on '{packageId}'.");
        }

        UnregisterAll(_registries.GetFor(tenantId), packageId, packageVersion);
        await _store.UpdateStateAsync(tenantId, packageId, packageVersion, PackageLoadState.Deactivated, null, ct);
        await AuditLifecycleAsync(tenantId, stored.Manifest, AuditCategories.PackageDeactivated,
            $"Package '{packageId}' v{packageVersion} deactivated.", null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Deactivated);
    }

    public async Task RehydrateActiveAsync(CancellationToken ct = default)
    {
        var allActive = await _store.GetByStateAllTenantsAsync(PackageLoadState.Active, ct);

        foreach (var tenantGroup in allActive.GroupBy(p => p.TenantId, StringComparer.OrdinalIgnoreCase))
        {
            // A package without a tenant cannot be rehydrated into any
            // tenant's registries; mark it failed rather than crash startup.
            if (string.IsNullOrWhiteSpace(tenantGroup.Key))
            {
                foreach (var orphan in tenantGroup)
                {
                    await _store.UpdateStateAsync(
                        orphan.TenantId, orphan.Manifest.PackageId, orphan.Manifest.PackageVersion,
                        PackageLoadState.Failed, "Rehydration failed: package has no tenant.", ct);
                }
                continue;
            }

            var registries = _registries.GetFor(tenantGroup.Key);
            var pending = tenantGroup.ToList();
            var registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Multi-pass: register packages whose dependencies are already in,
            // until no progress. Leftovers have unresolvable dependencies.
            var progressed = true;
            while (progressed && pending.Count > 0)
            {
                progressed = false;

                foreach (var package in pending.ToList())
                {
                    var ready = package.Manifest.Dependencies.All(d => registeredIds.Contains(d.PackageId));
                    if (!ready)
                    {
                        continue;
                    }

                    try
                    {
                        RegisterAll(registries, package.Manifest);
                        registeredIds.Add(package.Manifest.PackageId);
                    }
                    catch (RegistryConflictException ex)
                    {
                        UnregisterAll(registries, package.Manifest.PackageId, package.Manifest.PackageVersion);
                        await _store.UpdateStateAsync(
                            tenantGroup.Key, package.Manifest.PackageId, package.Manifest.PackageVersion,
                            PackageLoadState.Failed, $"Rehydration conflict: {ex.Message}", ct);
                    }

                    pending.Remove(package);
                    progressed = true;
                }
            }

            foreach (var unresolved in pending)
            {
                await _store.UpdateStateAsync(
                    tenantGroup.Key, unresolved.Manifest.PackageId, unresolved.Manifest.PackageVersion,
                    PackageLoadState.Failed, "Rehydration failed: dependency is no longer active.", ct);
            }
        }
    }

    private async Task<List<string>> ResolveDependenciesAsync(string tenantId, KnowledgePackageManifest manifest, CancellationToken ct)
    {
        var errors = new List<string>();
        if (manifest.Dependencies.Count == 0)
        {
            return errors;
        }

        var actives = await _store.GetByStateAsync(tenantId, PackageLoadState.Active, ct);

        foreach (var dependency in manifest.Dependencies)
        {
            var candidates = actives
                .Where(p => string.Equals(p.Manifest.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                errors.Add($"Dependency '{dependency.PackageId}' is not active.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                && !candidates.Any(p => VersionAtLeast(p.Manifest.PackageVersion, dependency.MinimumVersion)))
            {
                errors.Add($"Dependency '{dependency.PackageId}' requires version >= {dependency.MinimumVersion}; no active version satisfies it.");
            }
        }

        return errors;
    }

    private static bool VersionAtLeast(string actual, string minimum)
    {
        return System.Version.TryParse(ManifestValidator.NormalizeVersion(actual), out var actualVersion)
            && System.Version.TryParse(ManifestValidator.NormalizeVersion(minimum), out var minimumVersion)
            && actualVersion >= minimumVersion;
    }

    private static void RegisterAll(TenantRegistries registries, KnowledgePackageManifest manifest)
    {
        foreach (var entity in manifest.Entities)
            registries.Entities.Register(entity, manifest.PackageId, manifest.PackageVersion);
        foreach (var @event in manifest.Events)
            registries.Events.Register(@event, manifest.PackageId, manifest.PackageVersion);
        foreach (var capability in manifest.Capabilities)
            registries.Capabilities.Register(capability, manifest.PackageId, manifest.PackageVersion);
        foreach (var template in manifest.SchemaTemplates)
            registries.SchemaTemplates.Register(template, manifest.PackageId, manifest.PackageVersion);
        foreach (var workflow in manifest.Workflows)
            registries.Workflows.Register(workflow, manifest.PackageId, manifest.PackageVersion);
        foreach (var framework in manifest.PerformanceFrameworks)
            registries.PerformanceFrameworks.Register(framework, manifest.PackageId, manifest.PackageVersion);
        foreach (var agent in manifest.Agents)
            registries.Agents.Register(agent, manifest.PackageId, manifest.PackageVersion);
        foreach (var rule in manifest.Rules)
            registries.Rules.Register(rule, manifest.PackageId, manifest.PackageVersion);
    }

    private static void UnregisterAll(TenantRegistries registries, string packageId, string packageVersion)
    {
        registries.Entities.UnregisterPackage(packageId, packageVersion);
        registries.Events.UnregisterPackage(packageId, packageVersion);
        registries.Capabilities.UnregisterPackage(packageId, packageVersion);
        registries.SchemaTemplates.UnregisterPackage(packageId, packageVersion);
        registries.PerformanceFrameworks.UnregisterPackage(packageId, packageVersion);
        registries.Workflows.UnregisterPackage(packageId, packageVersion);
        registries.Agents.UnregisterPackage(packageId, packageVersion);
        registries.Rules.UnregisterPackage(packageId, packageVersion);
    }

    private Task AuditLifecycleAsync(
        string tenantId, KnowledgePackageManifest manifest, string category, string summary, string? detail, CancellationToken ct)
    {
        var entry = new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = manifest.EntityId,
            Category = category,
            Summary = summary,
            Detail = new Dictionary<string, string>
            {
                ["packageId"] = manifest.PackageId,
                ["packageVersion"] = manifest.PackageVersion
            }
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            entry.Detail["detail"] = detail;
        }

        return _audit.AppendAsync(entry, ct);
    }
}
