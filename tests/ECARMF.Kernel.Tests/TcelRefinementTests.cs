using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// TCEL (Tenant 9) framework refinements. Phase 1: the warnings channel
/// (P1.3) and dependency-cycle detection (P1.1) — the two fixes documentation
/// alone never solved.
/// </summary>
public class TcelRefinementTests
{
    private const string Tenant = "tcel";

    private readonly InMemoryPackageStore _store = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InMemoryAuditLog _audit = new();

    private PackageLoader CreateLoader() => new(_store, _registries, _audit);

    /// <summary>Minimal valid manifest — id/name/version is enough to validate;
    /// content is irrelevant to dependency-graph shape.</summary>
    private static KnowledgePackageManifest Pkg(string id, string version, params string[] deps) => new()
    {
        PackageId = id,
        Name = id,
        PackageVersion = version,
        Publisher = "TCEL tests",
        Dependencies = deps.Select(d => new PackageDependency { PackageId = d }).ToList()
    };

    // ---- P1.3 warnings channel ----

    [Fact]
    public void P1_3_result_can_succeed_with_warnings()
    {
        var ok = PackageOperationResult.Ok(PackageLoadState.Active, new[] { "overlap advisory" });
        Assert.True(ok.Success);
        Assert.Single(ok.Warnings);

        var attached = PackageOperationResult.Ok(PackageLoadState.Staged).WithWarnings(new[] { "w1", "w2" });
        Assert.True(attached.Success);
        Assert.Equal(2, attached.Warnings.Count);

        // Default is empty and never null — the backwards-compatible shape.
        Assert.Empty(PackageOperationResult.Fail(null, "boom").Warnings);
    }

    // ---- P1.1 self-dependency (static, no store) ----

    [Fact]
    public void P1_1_self_dependency_is_a_validation_error()
    {
        var errors = ManifestValidator.Validate(Pkg("pkg.self", "1.0.0", "pkg.self"), new EventRegistry());
        Assert.Contains(errors, e => e.Contains("pkg.self") && e.Contains("dependency on itself"));
    }

    [Fact]
    public async Task P1_1_self_dependency_load_fails()
    {
        var loader = CreateLoader();
        var result = await loader.LoadAsync(Tenant, Pkg("pkg.self", "1.0.0", "pkg.self"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("dependency on itself"));
    }

    // ---- P1.1 cross-package cycles ----

    [Fact]
    public async Task P1_1_two_package_cycle_is_rejected_with_the_named_path()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, Pkg("pkg.a", "1.0.0"));
        await loader.LoadAsync(Tenant, Pkg("pkg.b", "1.0.0", "pkg.a")); // b -> a, fine

        // A new version of a that depends on b closes the loop a -> b -> a.
        var result = await loader.LoadAsync(Tenant, Pkg("pkg.a", "2.0.0", "pkg.b"));

        Assert.False(result.Success);
        Assert.Equal(PackageLoadState.Failed, result.State);
        var error = Assert.Single(result.Errors);
        Assert.Contains("cycle", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pkg.a", error);
        Assert.Contains("pkg.b", error);
        Assert.Contains("→", error); // the full path, not a bare "cycle detected"
    }

    [Fact]
    public async Task P1_1_three_package_cycle_is_rejected()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, Pkg("p.x", "1.0.0"));
        await loader.LoadAsync(Tenant, Pkg("p.y", "1.0.0", "p.x"));
        await loader.LoadAsync(Tenant, Pkg("p.z", "1.0.0", "p.y"));

        // x -> z closes x -> z -> y -> x.
        var result = await loader.LoadAsync(Tenant, Pkg("p.x", "2.0.0", "p.z"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("p.x") && e.Contains("p.z") && e.Contains("p.y"));
    }

    [Fact]
    public async Task P1_1_strictly_cumulative_dependencies_never_trip_the_cycle_check()
    {
        var loader = CreateLoader();
        // The pattern the TCEL waves used successfully: each depends only on
        // lower-numbered ones. Provably acyclic; all must stage.
        Assert.True((await loader.LoadAsync(Tenant, Pkg("c.1", "1.0.0"))).Success);
        Assert.True((await loader.LoadAsync(Tenant, Pkg("c.2", "1.0.0", "c.1"))).Success);
        Assert.True((await loader.LoadAsync(Tenant, Pkg("c.3", "1.0.0", "c.1", "c.2"))).Success);
        Assert.True((await loader.LoadAsync(Tenant, Pkg("c.4", "1.0.0", "c.2", "c.3"))).Success);
    }

    [Fact]
    public async Task P1_1_cycle_among_other_packages_does_not_block_an_unrelated_load()
    {
        var loader = CreateLoader();
        // Pre-existing (hypothetical) mutual pair loaded before the checks would
        // have caught them — simulate by seeding the store directly.
        await _store.AddAsync(Tenant, Pkg("bad.a", "1.0.0", "bad.b"), PackageLoadState.Staged, null);
        await _store.AddAsync(Tenant, Pkg("bad.b", "1.0.0", "bad.a"), PackageLoadState.Staged, null);

        // An unrelated new package must still load — the pre-existing cycle
        // doesn't include it, so it is not this load's problem.
        var result = await loader.LoadAsync(Tenant, Pkg("good.c", "1.0.0"));
        Assert.True(result.Success);
    }

    // ---- Regression: the 23 shipped packages ----

    [Fact]
    public void Shipped_packages_declare_no_self_dependency_and_no_dependency_cycle()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var manifests = new List<KnowledgePackageManifest>();
        foreach (var file in Directory.EnumerateFiles(FindPackagesDir(), "*.json"))
        {
            var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(File.ReadAllText(file), options);
            Assert.NotNull(manifest);
            Assert.False(string.IsNullOrWhiteSpace(manifest!.PackageId), $"{Path.GetFileName(file)} has no packageId.");
            manifests.Add(manifest);

            Assert.DoesNotContain(manifest.Dependencies, d =>
                string.Equals(d.PackageId, manifest.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(manifests.Count >= 20, $"Expected the shipped package set; found {manifests.Count}.");

        // Whole-set acyclicity: the new cycle check must never fire on real content.
        var graph = manifests
            .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(m => m.Dependencies.Select(d => d.PackageId)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        Assert.False(HasCycle(graph), "The shipped packages form a dependency cycle.");
    }

    private static bool HasCycle(Dictionary<string, HashSet<string>> graph)
    {
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=white,1=gray,2=black

        bool Visit(string node)
        {
            color[node] = 1;
            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var next in deps)
                {
                    var c = color.GetValueOrDefault(next, 0);
                    if (c == 1) return true;                 // back-edge => cycle
                    if (c == 0 && graph.ContainsKey(next) && Visit(next)) return true;
                }
            }
            color[node] = 2;
            return false;
        }

        return graph.Keys.Any(n => color.GetValueOrDefault(n, 0) == 0 && Visit(n));
    }

    private static string FindPackagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "packages");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "treasury-controls-v1.json")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository 'packages' directory from the test base path.");
    }
}
