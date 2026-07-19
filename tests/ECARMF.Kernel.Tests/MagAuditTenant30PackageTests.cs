using System.Text.Json;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Tests;

public class MagAuditTenant30PackageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "tenant-30-mag-audit", "PACKAGE_CATALOG.json")))
            directory = directory.Parent;
        return directory ?? throw new DirectoryNotFoundException("ECARMF repository root not found.");
    }

    [Fact]
    public void Tenant_30_profile_is_regulated_and_not_a_taxpayer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName, "docs", "tenant-30-mag-audit", "TENANT_PROFILE.json")));
        var root = document.RootElement;

        Assert.Equal(30, root.GetProperty("tenantNumber").GetInt32());
        Assert.Equal("tenant-30-mag-audit", root.GetProperty("tenantId").GetString());
        Assert.Equal(SensitivityTiers.Regulated, root.GetProperty("sensitivityTier").GetString());
        Assert.Contains("Taxpayers are clients", root.GetProperty("notes").GetString());
    }

    [Fact]
    public void Catalog_declares_all_17_packages_in_strict_order()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName, "docs", "tenant-30-mag-audit", "PACKAGE_CATALOG.json")));
        var packages = document.RootElement.GetProperty("packages").EnumerateArray().ToList();

        Assert.Equal(17, packages.Count);
        Assert.Equal(Enumerable.Range(1, 17), packages.Select(p => p.GetProperty("order").GetInt32()));
        Assert.Equal(17, packages.Select(p => p.GetProperty("packageId").GetString()).Distinct().Count());
    }

    [Fact]
    public void Every_catalog_package_has_a_valid_manifest_and_backward_dependency()
    {
        var root = RepositoryRoot();
        using var catalogDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root.FullName, "docs", "tenant-30-mag-audit", "PACKAGE_CATALOG.json")));
        var catalog = catalogDocument.RootElement.GetProperty("packages").EnumerateArray().ToList();
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog)
        {
            var packageId = entry.GetProperty("packageId").GetString()!;
            var match = Directory.GetFiles(Path.Combine(root.FullName, "packages"), "mag-audit-*.json")
                .Select(path => (path, manifest: JsonSerializer.Deserialize<KnowledgePackageManifest>(File.ReadAllText(path), JsonOptions)))
                .Single(pair => pair.manifest?.PackageId == packageId);

            Assert.NotNull(match.manifest);
            Assert.Equal(entry.GetProperty("version").GetString(), match.manifest!.PackageVersion);
            Assert.Empty(ManifestValidator.Validate(match.manifest, new EventRegistry()));
            Assert.All(match.manifest.Dependencies, dependency => Assert.Contains(dependency.PackageId, installed));
            installed.Add(packageId);
        }
    }

    [Fact]
    public void Irs_and_idor_are_separate_and_idor_is_fail_closed()
    {
        var root = RepositoryRoot().FullName;
        var irs = File.ReadAllText(Path.Combine(root, "packages", "mag-audit-irs-engine-v1.json"));
        var idor = File.ReadAllText(Path.Combine(root, "packages", "mag-audit-idor-engine-v0.json"));

        Assert.Contains("IRS-AIKB", irs);
        Assert.Contains("Fail closed", idor);
        Assert.Contains("no IRS rule may be substituted", idor);
    }

    [Fact]
    public void Coding_handoff_registry_covers_every_package_and_backlog_item()
    {
        var root = RepositoryRoot().FullName;
        using var catalogDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "docs", "tenant-30-mag-audit", "PACKAGE_CATALOG.json")));
        using var registryDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "docs", "tenant-30-mag-audit", "COMPONENT_REGISTRY.json")));
        using var deliveryDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "docs", "tenant-30-mag-audit", "DELIVERY_MANIFEST.json")));

        var catalogIds = catalogDocument.RootElement.GetProperty("packages").EnumerateArray()
            .Select(p => p.GetProperty("packageId").GetString()).ToHashSet();
        var registryPackages = registryDocument.RootElement.GetProperty("packages").EnumerateArray().ToList();
        var registryIds = registryPackages.Select(p => p.GetProperty("packageId").GetString()).ToHashSet();

        Assert.Equal(catalogIds, registryIds);
        Assert.All(registryPackages, p =>
        {
            Assert.NotEmpty(p.GetProperty("components").EnumerateArray());
            Assert.NotEmpty(p.GetProperty("existingAssets").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("maturity").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("next").GetString()));
        });
        Assert.Equal(17, deliveryDocument.RootElement.GetProperty("packageCount").GetInt32());

        var backlogLines = File.ReadAllLines(Path.Combine(root, "docs", "tenant-30-mag-audit", "IMPLEMENTATION_BACKLOG.csv"))
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.Equal(31, backlogLines.Length); // header + 30 ordered work items
        Assert.Equal(30, deliveryDocument.RootElement.GetProperty("backlogCount").GetInt32());
    }
}
