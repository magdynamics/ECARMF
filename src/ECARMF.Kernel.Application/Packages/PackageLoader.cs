using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Packages;

public class PackageLoader : IPackageLoader
{
    private readonly IPackageStore _store;
    private readonly IEntityRegistry _entities;
    private readonly IRuleRegistry _rules;
    private readonly IEventRegistry _events;
    private readonly ICapabilityRegistry _capabilities;

    public PackageLoader(
        IPackageStore store,
        IEntityRegistry entities,
        IRuleRegistry rules,
        IEventRegistry events,
        ICapabilityRegistry capabilities)
    {
        _store = store;
        _entities = entities;
        _rules = rules;
        _events = events;
        _capabilities = capabilities;
    }

    public async Task<PackageOperationResult> LoadAsync(KnowledgePackageManifest manifest, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(manifest.PackageId)
            && !string.IsNullOrWhiteSpace(manifest.PackageVersion)
            && await _store.ExistsAsync(manifest.PackageId, manifest.PackageVersion, ct))
        {
            return PackageOperationResult.Fail(null,
                $"Package '{manifest.PackageId}' version '{manifest.PackageVersion}' is already loaded.");
        }

        var errors = ManifestValidator.Validate(manifest, _events);

        if (errors.Count > 0)
        {
            // Persist the failed attempt only when it is identifiable; an
            // unidentifiable manifest cannot satisfy the unique package index.
            if (!string.IsNullOrWhiteSpace(manifest.PackageId) && !string.IsNullOrWhiteSpace(manifest.PackageVersion))
            {
                await _store.AddAsync(manifest, PackageLoadState.Failed, string.Join(" ", errors), ct);
            }

            return PackageOperationResult.Fail(PackageLoadState.Failed, errors);
        }

        await _store.AddAsync(manifest, PackageLoadState.Staged, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Staged);
    }

    public async Task<PackageOperationResult> ActivateAsync(string packageId, string packageVersion, CancellationToken ct = default)
    {
        var stored = await _store.GetAsync(packageId, packageVersion, ct);
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

        var dependencyErrors = await ResolveDependenciesAsync(stored.Manifest, ct);
        if (dependencyErrors.Count > 0)
        {
            return PackageOperationResult.Fail(stored.State, dependencyErrors);
        }

        try
        {
            RegisterAll(stored.Manifest);
        }
        catch (RegistryConflictException ex)
        {
            UnregisterAll(packageId, packageVersion);
            await _store.UpdateStateAsync(packageId, packageVersion, PackageLoadState.Failed, ex.Message, ct);
            return PackageOperationResult.Fail(PackageLoadState.Failed, ex.Message);
        }

        await _store.UpdateStateAsync(packageId, packageVersion, PackageLoadState.Active, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Active);
    }

    public async Task<PackageOperationResult> DeactivateAsync(string packageId, string packageVersion, CancellationToken ct = default)
    {
        var stored = await _store.GetAsync(packageId, packageVersion, ct);
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

        var actives = await _store.GetByStateAsync(PackageLoadState.Active, ct);
        var dependent = actives.FirstOrDefault(p =>
            !string.Equals(p.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && p.Manifest.Dependencies.Any(d =>
                string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase)));

        if (dependent is not null)
        {
            return PackageOperationResult.Fail(stored.State,
                $"Cannot deactivate: active package '{dependent.Manifest.PackageId}' version '{dependent.Manifest.PackageVersion}' depends on '{packageId}'.");
        }

        UnregisterAll(packageId, packageVersion);
        await _store.UpdateStateAsync(packageId, packageVersion, PackageLoadState.Deactivated, null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Deactivated);
    }

    public async Task RehydrateActiveAsync(CancellationToken ct = default)
    {
        var pending = (await _store.GetByStateAsync(PackageLoadState.Active, ct)).ToList();
        var registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Multi-pass: register packages whose dependencies are already in, until
        // no progress. Leftovers have unresolvable dependencies and are failed.
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
                    RegisterAll(package.Manifest);
                    registeredIds.Add(package.Manifest.PackageId);
                }
                catch (RegistryConflictException ex)
                {
                    UnregisterAll(package.Manifest.PackageId, package.Manifest.PackageVersion);
                    await _store.UpdateStateAsync(
                        package.Manifest.PackageId, package.Manifest.PackageVersion,
                        PackageLoadState.Failed, $"Rehydration conflict: {ex.Message}", ct);
                }

                pending.Remove(package);
                progressed = true;
            }
        }

        foreach (var unresolved in pending)
        {
            await _store.UpdateStateAsync(
                unresolved.Manifest.PackageId, unresolved.Manifest.PackageVersion,
                PackageLoadState.Failed, "Rehydration failed: dependency is no longer active.", ct);
        }
    }

    private async Task<List<string>> ResolveDependenciesAsync(KnowledgePackageManifest manifest, CancellationToken ct)
    {
        var errors = new List<string>();
        if (manifest.Dependencies.Count == 0)
        {
            return errors;
        }

        var actives = await _store.GetByStateAsync(PackageLoadState.Active, ct);

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

    private void RegisterAll(KnowledgePackageManifest manifest)
    {
        foreach (var entity in manifest.Entities)
            _entities.Register(entity, manifest.PackageId, manifest.PackageVersion);
        foreach (var @event in manifest.Events)
            _events.Register(@event, manifest.PackageId, manifest.PackageVersion);
        foreach (var capability in manifest.Capabilities)
            _capabilities.Register(capability, manifest.PackageId, manifest.PackageVersion);
        foreach (var rule in manifest.Rules)
            _rules.Register(rule, manifest.PackageId, manifest.PackageVersion);
    }

    private void UnregisterAll(string packageId, string packageVersion)
    {
        _entities.UnregisterPackage(packageId, packageVersion);
        _events.UnregisterPackage(packageId, packageVersion);
        _capabilities.UnregisterPackage(packageId, packageVersion);
        _rules.UnregisterPackage(packageId, packageVersion);
    }
}
