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

        // Cross-package dependency cycle detection (TCEL P1.1). The static
        // validator cannot see other packages; here we have the store. A cycle
        // is only NEW if it runs through the incoming package, so we reject the
        // load when this manifest closes a loop and name the full path — a bare
        // "cycle detected" was never enough to resolve the TCEL clusters.
        var cyclePath = await DetectIncomingCycleAsync(tenantId, manifest, ct);
        if (cyclePath is not null)
        {
            var cycleError =
                $"Activating '{manifest.PackageId}' would create a dependency cycle: {string.Join(" → ", cyclePath)}. " +
                "A forward/loose coupling must be expressed in the description, never as a package dependency.";
            await _store.AddAsync(tenantId, manifest, PackageLoadState.Failed, cycleError, ct);
            await AuditLifecycleAsync(tenantId, manifest, AuditCategories.PackageFailed,
                $"Package '{manifest.PackageId}' v{manifest.PackageVersion} rejected: dependency cycle.", cycleError, ct);
            return PackageOperationResult.Fail(PackageLoadState.Failed, cycleError);
        }

        await _store.AddAsync(tenantId, manifest, PackageLoadState.Staged, null, ct);
        await AuditLifecycleAsync(tenantId, manifest, AuditCategories.PackageLoaded,
            $"Package '{manifest.PackageId}' v{manifest.PackageVersion} staged.", null, ct);
        return PackageOperationResult.Ok(PackageLoadState.Staged, ManifestValidator.CollectWarnings(manifest));
    }

    public async Task<PackageOperationResult> ActivateAsync(string tenantId, string packageId, string packageVersion, CancellationToken ct = default)
    {
        var stored = await _store.GetAsync(tenantId, packageId, packageVersion, ct);
        if (stored is null)
        {
            return PackageOperationResult.Fail(null,
                $"Package '{packageId}' version '{packageVersion}' is not loaded.");
        }

        // Failed is retryable ONLY when the failure came from activation (a
        // registry conflict with whatever else was active at the time) — that
        // is environmental and may have been resolved since. A validation
        // failure at load time is permanent for the version: fix the manifest
        // and publish a new version.
        if (stored.State is not (PackageLoadState.Staged or PackageLoadState.Deactivated or PackageLoadState.Failed))
        {
            return PackageOperationResult.Fail(stored.State,
                $"Package '{packageId}' version '{packageVersion}' cannot be activated from state '{stored.State}'.");
        }

        if (stored.State == PackageLoadState.Failed
            && ManifestValidator.Validate(stored.Manifest, _registries.GetFor(tenantId).Events).Count > 0)
        {
            return PackageOperationResult.Fail(stored.State,
                $"Package '{packageId}' version '{packageVersion}' failed validation; publish a corrected version.");
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

        // Supersede resolution (TCEL P2.1): warn — do NOT auto-deactivate. A
        // superseded package that is still active is a deliberate operator
        // decision to unwind, not a side effect of activating its replacement.
        var supersedeWarnings = await ResolveSupersedesAsync(tenantId, stored.Manifest, ct);
        return PackageOperationResult.Ok(PackageLoadState.Active, supersedeWarnings);
    }

    private async Task<IReadOnlyList<string>> ResolveSupersedesAsync(
        string tenantId, KnowledgePackageManifest manifest, CancellationToken ct)
    {
        if (manifest.Supersedes.Count == 0)
        {
            return [];
        }

        var actives = await _store.GetByStateAsync(tenantId, PackageLoadState.Active, ct);
        var warnings = new List<string>();

        foreach (var reference in manifest.Supersedes)
        {
            var stillActive = actives.Where(p =>
                string.Equals(p.Manifest.PackageId, reference.PackageId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(reference.PackageVersion)
                    || string.Equals(p.Manifest.PackageVersion, reference.PackageVersion, StringComparison.OrdinalIgnoreCase)));

            foreach (var superseded in stillActive)
            {
                warnings.Add(
                    $"'{manifest.PackageId}' v{manifest.PackageVersion} supersedes '{superseded.Manifest.PackageId}' " +
                    $"v{superseded.Manifest.PackageVersion}, which is still active — deactivate it deliberately.");
                await AuditLifecycleAsync(tenantId, manifest, AuditCategories.PackageSuperseded,
                    $"Package '{manifest.PackageId}' v{manifest.PackageVersion} supersedes still-active " +
                    $"'{superseded.Manifest.PackageId}' v{superseded.Manifest.PackageVersion}.",
                    "Superseded package remains active until the operator deactivates it.", ct);
            }
        }

        return warnings;
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

    /// <summary>
    /// Returns the dependency cycle the incoming manifest would close, as a
    /// path of package ids ending back at the incoming id (e.g. [a, b, a]), or
    /// null when it introduces no cycle. Builds the directed graph
    /// packageId → dependency packageIds over every stored package for the
    /// tenant (any state, collapsed across versions) plus the incoming
    /// manifest, then walks out-edges from the incoming node: a directed cycle
    /// through a node is always re-entered by following its out-edges. A cycle
    /// that does NOT include the incoming package is pre-existing state and is
    /// deliberately ignored here (it never blocks an unrelated load).
    /// </summary>
    private async Task<IReadOnlyList<string>?> DetectIncomingCycleAsync(
        string tenantId, KnowledgePackageManifest manifest, CancellationToken ct)
    {
        var incomingId = manifest.PackageId;
        if (string.IsNullOrWhiteSpace(incomingId) || manifest.Dependencies.Count == 0)
        {
            return null;
        }

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void AddEdges(string from, IEnumerable<string> to)
        {
            if (string.IsNullOrWhiteSpace(from)) return;
            if (!adjacency.TryGetValue(from, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adjacency[from] = set;
            }
            foreach (var t in to.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                set.Add(t);
            }
        }

        foreach (var stored in await _store.GetAllAsync(tenantId, ct))
        {
            AddEdges(stored.Manifest.PackageId, stored.Manifest.Dependencies.Select(d => d.PackageId));
        }
        // The incoming manifest's edges override/extend any stored version's.
        AddEdges(incomingId, manifest.Dependencies.Select(d => d.PackageId));

        var path = new List<string>();
        var inPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Walk(string node)
        {
            path.Add(node);
            inPath.Add(node);

            if (adjacency.TryGetValue(node, out var deps))
            {
                foreach (var next in deps)
                {
                    if (string.Equals(next, incomingId, StringComparison.OrdinalIgnoreCase))
                    {
                        path.Add(incomingId); // close the loop for a readable path
                        return true;
                    }
                    // A node already on the current path (but not the incoming
                    // one) is a pre-existing cycle — skip it, don't recurse.
                    if (inPath.Contains(next) || explored.Contains(next))
                    {
                        continue;
                    }
                    if (Walk(next))
                    {
                        return true;
                    }
                }
            }

            inPath.Remove(node);
            explored.Add(node);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        return Walk(incomingId) ? path : null;
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
        foreach (var asset in manifest.KnowledgeAssets)
            registries.KnowledgeAssets.Register(asset, manifest.PackageId, manifest.PackageVersion);
        foreach (var extraction in manifest.AiExtractionTemplates)
            registries.AiExtractionTemplates.Register(extraction, manifest.PackageId, manifest.PackageVersion);
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
        registries.KnowledgeAssets.UnregisterPackage(packageId, packageVersion);
        registries.AiExtractionTemplates.UnregisterPackage(packageId, packageVersion);
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
