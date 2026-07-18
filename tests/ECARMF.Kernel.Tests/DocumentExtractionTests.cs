using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The document-extraction agent: unstructured document -> the raw
/// payload the connector's template expects -> normal ingestion.</summary>
public class DocumentExtractionTests
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
    private readonly FakeLanguageModelClient _llm = new();

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

    private async Task<DocumentExtractionService> CreateAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest("connector-reference-templates-v1.json");
        Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
        Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);

        await _connectors.EnsureSeedConnectorsAsync(Tenant);
        var intake = new TransactionIntakeService(_records, _bus, _registries, _audit);
        var ingestion = new ConnectorIngestionService(_connectors, _registries, intake);
        return new DocumentExtractionService(
            _connectors, _registries, ingestion, new FakeLanguageModelProvider(_llm), _audit);
    }

    [Fact]
    public async Task Model_extracts_a_document_into_the_connector_payload_and_normal_ingestion_runs()
    {
        var service = await CreateAsync();
        _llm.IsConfigured = true;
        _llm.Response = """
            ```json
            { "opportunityType": "RealEstateAcquisition", "title": "Riverside warehouse", "estimatedValue": 2500000 }
            ```
            """;

        var result = await service.ExtractAndIngestAsync(
            Tenant, SeedConnectors.ManualEntry, "broker-email.txt",
            "FW: off-market deal — riverside warehouse, asking ~2.5M, seller motivated...",
            "owner@platform");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("llm:fake-model", result.Backend);
        // The extraction prompt describes the template's raw fields.
        Assert.Contains("opportunityType", _llm.LastUserPrompt);
        Assert.Contains("riverside warehouse", _llm.LastUserPrompt);

        var record = _records.Items.Single();
        Assert.Equal("Opportunity", record.TransactionType);
        Assert.Equal("owner@platform", record.SubmittedBy);          // human stays the submitter
        Assert.Equal("2500000", record.Payload["estimatedValue"]);
        Assert.Equal(Provenance.HumanEntered, record.Payload["provenance"]); // connector's class

        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.DocumentExtracted);
        Assert.Equal(DocumentExtractionService.ActorIdentifier, audit.Actor);
        Assert.Equal(record.TransactionId.ToString(), audit.Detail["recordIds"]);
    }

    [Fact]
    public async Task Text_template_extracts_deterministically_without_a_model()
    {
        var service = await CreateAsync();
        await _connectors.AddAsync(Tenant, new ConnectorDefinition(
            "bank-drop", "Bank statement drop", "BankFeed", "file",
            "bank-mt940-text", 0.95m, Provenance.ExternalSystemVerified, "Active"));
        // No LLM configured — the template's regex patterns are the extractor.

        var result = await service.ExtractAndIngestAsync(
            Tenant, "bank-drop", "statement.txt",
            ":61:2606150615D15000,00NTRFNONREF//Solar inverter payment\n:86:Beneficiary: SunridgeEnergy Equipment Supplier",
            "admin@platform");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("regex-template", result.Backend);
        var record = _records.Items.Single();
        Assert.Equal("15000.00", record.Payload["amount"]);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.DocumentExtracted);
    }

    [Fact]
    public async Task Json_template_without_a_model_fails_with_a_helpful_error()
    {
        var service = await CreateAsync();

        var result = await service.ExtractAndIngestAsync(
            Tenant, SeedConnectors.ManualEntry, "email.txt", "some deal text", "owner@platform");

        Assert.False(result.Success);
        // The message must point the user to Setup → AI Backend and make clear
        // a local server needs no key (the fix for MAG's "why Anthropic?" confusion).
        Assert.Contains(result.Errors, e => e.Contains("Setup → AI Backend") && e.Contains("no key"));
        Assert.Empty(_records.Items);
    }

    [Fact]
    public async Task Invalid_model_output_is_rejected_not_ingested()
    {
        var service = await CreateAsync();
        _llm.IsConfigured = true;
        _llm.Response = "I could not find any structured data in this document.";

        var result = await service.ExtractAndIngestAsync(
            Tenant, SeedConnectors.ManualEntry, "email.txt", "unrelated text", "owner@platform");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("did not return a valid json"));
        Assert.Empty(_records.Items);
    }

    [Fact]
    public async Task Missing_required_fields_from_extraction_surface_as_mapping_errors()
    {
        var service = await CreateAsync();
        _llm.IsConfigured = true;
        _llm.Response = """{ "title": "No value or type in the document" }""";

        var result = await service.ExtractAndIngestAsync(
            Tenant, SeedConnectors.ManualEntry, "email.txt", "vague pitch", "owner@platform");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Required field"));
        Assert.Empty(_records.Items);
        // The failed attempt is still audited — silence is a failure mode.
        var audit = Assert.Single(_audit.Items, a => a.Category == AuditCategories.DocumentExtracted);
        Assert.Equal("False", audit.Detail["ingestionSuccess"]);
    }
}
