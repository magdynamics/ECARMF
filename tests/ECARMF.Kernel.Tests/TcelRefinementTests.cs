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

    // ---- P1.2 ID ledger ----

    private static KnowledgePackageManifest PkgWithRule(string id, string version, string ruleId) => new()
    {
        PackageId = id,
        Name = id,
        PackageVersion = version,
        Publisher = "TCEL tests",
        Events = [new EventDeclaration { EventName = $"{id}.Ev" }],
        Rules =
        [
            new RuleDeclaration
            {
                RuleId = ruleId, Name = "r", TriggerEvent = $"{id}.Ev",
                OutcomeOnMatch = "Flagged", ReasonTemplate = "x"
            }
        ]
    };

    [Fact]
    public async Task P1_2_ledger_lists_every_id_with_provenance_before_activation()
    {
        var loader = CreateLoader();
        // Both merely STAGED — staging never touches the registries, so a
        // collision would not yet be rejected. The ledger must show it anyway.
        await loader.LoadAsync(Tenant, PkgWithRule("pkg.one", "1.0.0", "shared.rule"));
        await loader.LoadAsync(Tenant, PkgWithRule("pkg.two", "1.0.0", "shared.rule"));

        var ledger = await new PackageIdLedgerService(_store).BuildAsync(Tenant);

        var shared = ledger.Ids["rules"].Single(e => e.Id == "shared.rule");
        Assert.Contains("pkg.one@1.0.0", shared.DeclaredBy);
        Assert.Contains("pkg.two@1.0.0", shared.DeclaredBy); // collision visible before activation rejects it
        Assert.Contains(ledger.Ids["events"], e => e.Id == "pkg.one.Ev");
    }

    [Fact]
    public async Task P1_2_ledger_warns_about_pre_existing_dependency_cycle()
    {
        await _store.AddAsync(Tenant, Pkg("cyc.a", "1.0.0", "cyc.b"), PackageLoadState.Staged, null);
        await _store.AddAsync(Tenant, Pkg("cyc.b", "1.0.0", "cyc.a"), PackageLoadState.Staged, null);

        var ledger = await new PackageIdLedgerService(_store).BuildAsync(Tenant);

        Assert.Contains(ledger.Warnings, w => w.Contains("cycle") && w.Contains("cyc.a") && w.Contains("cyc.b"));
    }

    // ---- P2.1 supersedes ----

    [Fact]
    public void P2_1_self_supersede_is_a_validation_error()
    {
        var m = Pkg("pkg.x", "2.0.0");
        m.Supersedes = [new PackageReference { PackageId = "pkg.x" }];
        var errors = ManifestValidator.Validate(m, new EventRegistry());
        Assert.Contains(errors, e => e.Contains("cannot supersede itself"));
    }

    [Fact]
    public async Task P2_1_superseding_a_still_active_package_warns_but_does_not_deactivate_it()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, Pkg("legacy.pkg", "1.0.0"));
        await loader.ActivateAsync(Tenant, "legacy.pkg", "1.0.0");

        var replacement = Pkg("new.pkg", "1.0.0");
        replacement.Supersedes = [new PackageReference { PackageId = "legacy.pkg" }];
        await loader.LoadAsync(Tenant, replacement);
        var result = await loader.ActivateAsync(Tenant, "new.pkg", "1.0.0");

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("legacy.pkg") && w.Contains("still active"));
        // Not auto-deactivated: the legacy package is untouched.
        var legacy = await _store.GetAsync(Tenant, "legacy.pkg", "1.0.0");
        Assert.Equal(PackageLoadState.Active, legacy!.State);
        Assert.Contains(_audit.Items, a => a.Category == "PackageSuperseded");
    }

    // ---- P2.2 agent Identity block ----

    [Fact]
    public async Task P2_2_agent_without_owner_loads_with_a_warning()
    {
        var loader = CreateLoader();
        var m = Pkg("agentpkg", "1.0.0");
        m.Agents = [new AgentDeclaration { AgentId = "coding.ai", Name = "Coding AI", Persona = "You review medical coding." }];

        var result = await loader.LoadAsync(Tenant, m);

        Assert.True(result.Success); // warning never blocks
        Assert.Contains(result.Warnings, w => w.Contains("coding.ai") && w.Contains("Owner"));
    }

    [Fact]
    public async Task P2_2_agent_with_full_identity_block_loads_clean()
    {
        var loader = CreateLoader();
        var m = Pkg("agentpkg2", "1.0.0");
        m.Agents =
        [
            new AgentDeclaration
            {
                AgentId = "hipaa.ai", Name = "HIPAA AI", Persona = "You advise on HIPAA.",
                Owner = "Compliance", IndependentValidator = "Internal Audit", RiskTier = "Regulated",
                Prohibited = ["never state a breach has legally occurred"]
            }
        ];

        var result = await loader.LoadAsync(Tenant, m);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Owner"));
    }

    // ---- P2.3 consolidates (schema half; behavior is PR4/P3.2) ----

    [Fact]
    public void P2_3_self_consolidate_is_a_validation_error()
    {
        var m = Pkg("master.pkg", "1.0.0");
        m.Consolidates = ["master.pkg"];
        var errors = ManifestValidator.Validate(m, new EventRegistry());
        Assert.Contains(errors, e => e.Contains("cannot consolidate itself"));
    }

    // ---- P3.1 agent semantic-overlap (warning only) ----

    private async Task ActivateAgentAsync(string packageId, AgentDeclaration agent)
    {
        var loader = CreateLoader();
        var m = Pkg(packageId, "1.0.0");
        m.Agents = [agent];
        await loader.LoadAsync(Tenant, m);
        await loader.ActivateAsync(Tenant, packageId, "1.0.0");
    }

    [Fact]
    public async Task P3_1_overlapping_agent_scope_warns_without_blocking()
    {
        await ActivateAgentAsync("exec.pkg", new AgentDeclaration
        {
            AgentId = "executive-advisor", Name = "Executive Advisor", Owner = "Exec",
            Description = "Synthesis of cross domain scores for executive board consumption.",
            Persona = "You produce an executive board briefing synthesizing cross domain signals."
        });

        var loader = CreateLoader();
        var incoming = Pkg("risk.pkg", "1.0.0");
        incoming.Agents =
        [
            new AgentDeclaration
            {
                AgentId = "executive-risk", Name = "Executive Risk", Owner = "Risk",
                Description = "Synthesis of cross domain risk for executive board consumption.",
                Persona = "You produce an executive board briefing synthesizing cross domain risk signals."
            }
        ];

        var result = await loader.LoadAsync(Tenant, incoming);

        Assert.True(result.Success); // overlap never blocks
        Assert.Contains(result.Warnings, w =>
            w.Contains("executive-risk") && w.Contains("executive-advisor") && w.Contains("scope terms"));
    }

    [Fact]
    public async Task P3_1_distinct_agents_do_not_warn()
    {
        await ActivateAgentAsync("coding.pkg", new AgentDeclaration
        {
            AgentId = "coding-quality", Name = "Coding Quality", Owner = "Coding",
            Description = "Reviews medical coding accuracy per coder.",
            Persona = "You assess procedure and diagnosis coding accuracy."
        });

        var loader = CreateLoader();
        var incoming = Pkg("cred.pkg", "1.0.0");
        incoming.Agents =
        [
            new AgentDeclaration
            {
                AgentId = "credentialing-status", Name = "Credentialing", Owner = "Cred",
                Description = "Tracks provider credentialing revalidation deadlines.",
                Persona = "You monitor payer enrollment and revalidation timelines."
            }
        ];

        var result = await loader.LoadAsync(Tenant, incoming);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("scope terms"));
    }

    // ---- P3.2 consolidation-is-real ----

    [Fact]
    public async Task P3_2_consolidating_an_unknown_package_is_a_load_error()
    {
        var loader = CreateLoader();
        var m = Pkg("master.catalog", "1.0.0");
        m.Consolidates = ["never.loaded"];

        var result = await loader.LoadAsync(Tenant, m);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("never.loaded") && e.Contains("never loaded"));
    }

    [Fact]
    public async Task P3_2_consolidation_without_any_reference_warns()
    {
        var loader = CreateLoader();
        // A real source package with a distinctive control id.
        await loader.LoadAsync(Tenant, PkgWithRule("source.controls", "1.0.0", "VR-042-distinct"));

        // A "master" that consolidates it but mentions none of its ids.
        var master = Pkg("master.catalog", "1.0.0");
        master.Consolidates = ["source.controls"];
        master.Capabilities = [new CapabilityDeclaration { CapabilityId = "master.cap", Name = "Master", Description = "freshly worded rows" }];

        var result = await loader.LoadAsync(Tenant, master);

        Assert.True(result.Success); // warning, not error
        Assert.Contains(result.Warnings, w => w.Contains("source.controls") && w.Contains("references none"));
    }

    [Fact]
    public async Task P3_2_consolidation_that_references_a_source_id_does_not_warn()
    {
        var loader = CreateLoader();
        await loader.LoadAsync(Tenant, PkgWithRule("source.controls", "1.0.0", "VR-042-distinct"));

        var master = Pkg("master.catalog", "1.0.0");
        master.Consolidates = ["source.controls"];
        // The master's own content actually references the source control id.
        master.Capabilities =
        [
            new CapabilityDeclaration { CapabilityId = "master.cap", Name = "Master", Description = "Rolls up VR-042-distinct and peers." }
        ];

        var result = await loader.LoadAsync(Tenant, master);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("references none"));
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
