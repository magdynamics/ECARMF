using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryConnectorStore : IConnectorStore
{
    private readonly List<(string TenantId, ConnectorDefinition Connector)> _items = [];

    public Task<ConnectorDefinition?> GetAsync(string tenantId, string connectorId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(i =>
            string.Equals(i.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.Connector.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase)).Connector);

    public Task<IReadOnlyList<ConnectorDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConnectorDefinition>>(
            _items.Where(i => string.Equals(i.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Connector).ToList());

    public Task AddAsync(string tenantId, ConnectorDefinition connector, CancellationToken ct = default)
    {
        _items.Add((tenantId, connector));
        return Task.CompletedTask;
    }

    public Task EnsureSeedConnectorsAsync(string tenantId, CancellationToken ct = default)
    {
        if (_items.All(i => !string.Equals(i.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)))
        {
            _items.Add((tenantId, new ConnectorDefinition(
                SeedConnectors.ManualEntry, "Manual Entry", "Manual", "manual",
                "manual-opportunity-json", 0.5m, Provenance.HumanEntered, "Active")));
        }
        return Task.CompletedTask;
    }
}

/// <summary>Connector ingestion: raw payload -> template mapping -> stamped
/// record -> the same immutable intake pipeline as everything else.</summary>
public class ConnectorIngestionTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryTransactionStore _records = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();
    private readonly InMemoryConnectorStore _connectors = new();

    private static KnowledgePackageManifest LoadManifest(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", fileName)))
        {
            directory = directory.Parent;
        }
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", fileName));
        return JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions)!;
    }

    private async Task<ConnectorIngestionService> CreateAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest("connector-reference-templates-v1.json");
        Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
        Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);

        await _connectors.EnsureSeedConnectorsAsync(Tenant);
        var intake = new TransactionIntakeService(_records, _bus, _registries, _audit);
        return new ConnectorIngestionService(_connectors, _registries, intake);
    }

    [Fact]
    public async Task Manual_entry_connector_maps_and_stamps_the_record()
    {
        var service = await CreateAsync();
        const string form = """
        { "opportunityType": "RealEstateAcquisition", "title": "Former Kmart site", "estimatedValue": 4200000 }
        """;

        var result = await service.IngestAsync(Tenant, SeedConnectors.ManualEntry, form, "owner@platform");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var record = _records.Items.Single();
        Assert.Equal("Opportunity", record.TransactionType);
        Assert.Equal("owner@platform", record.SubmittedBy);
        // The ingestion stamp, applied before validation on every record.
        Assert.Equal(SeedConnectors.ManualEntry, record.Payload["sourceId"]);
        Assert.Equal("Manual", record.Payload["sourceCategory"]);
        Assert.Equal(Provenance.HumanEntered, record.Payload["provenance"]);
        Assert.Equal("0.5", record.Payload["reliabilityRating"]);
        Assert.True(record.Payload.ContainsKey("ingestedAt"));
        Assert.Equal("1.0.0", record.Payload["schemaTemplateVersion"]);
    }

    [Fact]
    public async Task Bank_connector_instance_reuses_the_bank_template()
    {
        var service = await CreateAsync();
        // Second bank = new connector instance reusing an existing template.
        await _connectors.AddAsync(Tenant, new ConnectorDefinition(
            "first-national-sftp", "First National SFTP", "BankFeed", "file",
            "bank-mt940-text", 0.95m, Provenance.ExternalSystemVerified, "Active"));

        var result = await service.IngestAsync(Tenant, "first-national-sftp",
            ":61:2606150615D15000,00NTRFNONREF//Solar inverter payment\n:86:Beneficiary: SunridgeEnergy Equipment Supplier",
            "admin@platform");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var record = _records.Items.Single();
        Assert.Equal("TreasuryEvent", record.TransactionType);
        Assert.Equal("Debit", record.Payload["direction"]);
        Assert.Equal("15000.00", record.Payload["amount"]);
        Assert.Equal(Provenance.ExternalSystemVerified, record.Payload["provenance"]);
        Assert.Equal("0.95", record.Payload["reliabilityRating"]);
    }

    [Fact]
    public async Task Unknown_connector_is_rejected()
    {
        var service = await CreateAsync();

        var result = await service.IngestAsync(Tenant, "no-such-connector", "{}", "admin@platform");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("not configured"));
    }

    [Fact]
    public async Task Connector_with_unregistered_template_is_rejected()
    {
        var service = await CreateAsync();
        await _connectors.AddAsync(Tenant, new ConnectorDefinition(
            "broken", "Broken", "Manual", "manual", "no-such-template", 0.5m, Provenance.HumanEntered, "Active"));

        var result = await service.IngestAsync(Tenant, "broken", "{}", "admin@platform");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("no-such-template"));
    }
}
