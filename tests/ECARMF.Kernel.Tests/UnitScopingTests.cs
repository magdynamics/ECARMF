using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Integrations;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// Unit-scoped data integrity: data entering the platform is attributed to a
/// real, Active organizational unit (Chase feed -> oak-lawn) or explicitly to
/// the whole tenant (HR guideline -> all units) — never to a unit that does
/// not exist, and a location's view is its own data plus the tenant-wide.
/// </summary>
public class UnitScopingTests
{
    private const string Tenant = "universal";

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
    private readonly InMemoryOrgUnitStore _units = new();
    private readonly InMemoryDocumentLibrary _library = new();

    private void SeedUnits()
    {
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "oak-lawn", Name = "Oak Lawn", UnitType = "Location" });
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "elgin", Name = "Elgin", UnitType = "Location" });
        _units.Items.Add(new OrganizationalUnit { TenantId = Tenant, UnitId = "closed-site", Name = "Closed", UnitType = "Location", Status = "Archived" });
    }

    private TransactionIntakeService Intake() => new(_records, _bus, _registries, _audit);

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

    private async Task<ConnectorIngestionService> CreateConnectorServiceAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest("connector-reference-templates-v1.json");
        Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
        Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);
        await _connectors.EnsureSeedConnectorsAsync(Tenant);
        return new ConnectorIngestionService(_connectors, _registries, Intake(), units: _units);
    }

    // ---- intake stamping ----

    [Fact]
    public async Task Intake_stamps_the_unit_into_column_and_payload()
    {
        SeedUnits();
        var receipt = await Intake().ReceiveAsync(new TransactionSubmission(
            Tenant, "BankLine", "owner@universal",
            new Dictionary<string, string> { ["amount"] = "125.00" }, UnitRef: "oak-lawn"));

        var record = _records.Items.Single(t => t.TransactionId == receipt.TransactionId);
        Assert.Equal("oak-lawn", record.UnitRef);
        Assert.Equal("oak-lawn", record.Payload["unitRef"]); // rules/KPIs can key on it
    }

    [Fact]
    public async Task Payload_mapped_unit_is_promoted_to_the_filterable_column()
    {
        await Intake().ReceiveAsync(new TransactionSubmission(
            Tenant, "BankLine", "owner@universal",
            new Dictionary<string, string> { ["unitRef"] = "elgin", ["amount"] = "80.00" }));

        Assert.Equal("elgin", _records.Items.Single().UnitRef);
    }

    [Fact]
    public async Task Tenant_wide_records_carry_no_unit()
    {
        await Intake().ReceiveAsync(new TransactionSubmission(
            Tenant, "HrGuideline", "owner@universal",
            new Dictionary<string, string> { ["title"] = "PTO policy" }));

        Assert.Null(_records.Items.Single().UnitRef);
    }

    // ---- integrity at the connector door ----

    [Fact]
    public async Task Connector_ingest_refuses_a_unit_that_does_not_exist()
    {
        SeedUnits();
        var service = await CreateConnectorServiceAsync();

        var result = await service.IngestAsync(Tenant, SeedConnectors.ManualEntry,
            """{ "opportunityType": "RealEstateAcquisition", "title": "x", "estimatedValue": 1 }""",
            "owner@universal", unitRef: "no-such-place");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
        Assert.Empty(_records.Items);
    }

    [Fact]
    public async Task Connector_ingest_refuses_an_archived_unit()
    {
        SeedUnits();
        var service = await CreateConnectorServiceAsync();

        var result = await service.IngestAsync(Tenant, SeedConnectors.ManualEntry,
            """{ "opportunityType": "RealEstateAcquisition", "title": "x", "estimatedValue": 1 }""",
            "owner@universal", unitRef: "closed-site");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Archived"));
    }

    [Fact]
    public async Task Connector_ingest_stamps_the_declared_unit_on_every_record()
    {
        SeedUnits();
        var service = await CreateConnectorServiceAsync();

        var result = await service.IngestAsync(Tenant, SeedConnectors.ManualEntry,
            """{ "opportunityType": "RealEstateAcquisition", "title": "Oak Lawn deal", "estimatedValue": 1 }""",
            "owner@universal", unitRef: "oak-lawn");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var record = _records.Items.Single();
        Assert.Equal("oak-lawn", record.UnitRef);
        Assert.Equal("oak-lawn", record.Payload["unitRef"]);
    }

    // ---- integration binding: the Chase -> Oak Lawn case ----

    [Fact]
    public async Task A_unit_bound_integration_stamps_its_unit_on_every_feed()
    {
        SeedUnits();
        var connectorService = await CreateConnectorServiceAsync();
        var integrations = new InMemoryIntegrationStore();
        integrations.Items.Add(new ECARMF.Kernel.Domain.Integrations.IntegrationDefinition
        {
            TenantId = Tenant,
            IntegrationId = "chase-oak-lawn",
            Name = "Chase — Oak Lawn",
            ApplicationType = "Banking",
            ConnectorId = SeedConnectors.ManualEntry,
            Mode = "push",
            UnitId = "oak-lawn",
        });
        var feeds = new IntegrationFeedService(integrations, connectorService, new FakeFeedPuller(), _audit);

        var run = await feeds.PushAsync(Tenant, "chase-oak-lawn",
            """{ "opportunityType": "RealEstateAcquisition", "title": "statement line", "estimatedValue": 1 }""",
            "system:feed");

        Assert.True(run.Success, run.Error);
        Assert.Equal("oak-lawn", _records.Items.Single().UnitRef);
    }

    // ---- reading: the inheritance rule ----

    [Fact]
    public async Task A_units_view_is_its_own_records_plus_tenant_wide_ones()
    {
        SeedUnits();
        var intake = Intake();
        await intake.ReceiveAsync(new TransactionSubmission(Tenant, "BankLine", "o",
            new Dictionary<string, string>(), UnitRef: "oak-lawn"));
        await intake.ReceiveAsync(new TransactionSubmission(Tenant, "BankLine", "o",
            new Dictionary<string, string>(), UnitRef: "elgin"));
        await intake.ReceiveAsync(new TransactionSubmission(Tenant, "HrGuideline", "o",
            new Dictionary<string, string>())); // tenant-wide

        var (oakView, _) = await _records.QueryAsync(new TransactionQuery(Tenant, UnitRef: "oak-lawn"));
        Assert.Equal(2, oakView.Count); // its bank line + the HR guideline
        Assert.DoesNotContain(oakView, t => t.UnitRef == "elgin");

        var (oakOnly, _) = await _records.QueryAsync(new TransactionQuery(Tenant, UnitRef: "oak-lawn", UnitExclusive: true));
        Assert.Single(oakOnly);
        Assert.Equal("oak-lawn", oakOnly[0].UnitRef);
    }

    // ---- bulk import ----

    [Fact]
    public async Task Bulk_import_attributes_rows_with_per_row_override()
    {
        SeedUnits();
        var import = new BulkImportService(Intake(), _library, _audit, _units);
        var csv = Encoding.UTF8.GetBytes(
            "amount,unitRef\n" +
            "10,\n" +          // falls back to the import-level unit
            "20,elgin\n");     // row override

        var result = await import.ImportCsvAsync(Tenant, "BankLine", "history.csv", csv, "o", unitRef: "oak-lawn");

        Assert.Equal(2, result.Imported);
        Assert.Contains(_records.Items, t => t.UnitRef == "oak-lawn");
        Assert.Contains(_records.Items, t => t.UnitRef == "elgin");
    }

    [Fact]
    public async Task Bulk_import_rejects_an_unknown_import_level_unit_up_front()
    {
        SeedUnits();
        var import = new BulkImportService(Intake(), _library, _audit, _units);
        var csv = Encoding.UTF8.GetBytes("amount\n10\n");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            import.ImportCsvAsync(Tenant, "BankLine", "h.csv", csv, "o", unitRef: "nowhere"));
        Assert.Empty(_records.Items);
    }

    [Fact]
    public async Task Bulk_import_fails_only_the_rows_with_an_unknown_unit()
    {
        SeedUnits();
        var import = new BulkImportService(Intake(), _library, _audit, _units);
        var csv = Encoding.UTF8.GetBytes(
            "amount,unitRef\n" +
            "10,oak-lawn\n" +
            "20,nowhere\n");

        var result = await import.ImportCsvAsync(Tenant, "BankLine", "h.csv", csv, "o");

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Errors, e => e.Contains("nowhere"));
        Assert.Equal("oak-lawn", _records.Items.Single().UnitRef);
    }

    // ---- output side: scores and alerts inherit the unit ----

    private readonly InMemoryOutcomeStore _outcomes = new();
    private readonly InMemoryScoreStore _scores = new();

    private async Task ActivatePackagesAsync(params string[] files)
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        foreach (var file in files)
        {
            var manifest = LoadManifest(file);
            Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
            Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);
        }
    }

    private async Task SubmitAndProcessAsync(Dictionary<string, string> payload, string? unitRef)
    {
        var receipt = await Intake().ReceiveAsync(new TransactionSubmission(
            Tenant, "withdrawal", "treasurer@universal", payload, UnitRef: unitRef));
        Assert.True(receipt.EventPublished);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());

        var processor = new ECARMF.Kernel.Application.Processing.EventProcessor(
            _registries, _outcomes, _scores, _bus, _audit,
            new ECARMF.Kernel.Application.Performance.PerformanceEvaluationService(_registries, _scores, _audit));
        await processor.ProcessAsync(enumerator.Current);
    }

    [Fact]
    public async Task Scores_computed_from_a_units_record_carry_that_unit()
    {
        SeedUnits();
        // treasury-controls declares RecordReceived; the AML rule emits an
        // AMLRisk score from the payload on that same event.
        await ActivatePackagesAsync("treasury-controls-v1.json", "compliance-aml-kyc-v1.json");

        await SubmitAndProcessAsync(new Dictionary<string, string>
        {
            ["ventureId"] = "V-001",
            ["amount"] = "500",
            ["amlRiskRating"] = "0.42",
            ["counterpartyId"] = "CP-9",
        }, unitRef: "oak-lawn");

        var score = _scores.Items.Single(s => s.ScoreType == "AMLRisk");
        Assert.Equal("oak-lawn", score.UnitRef); // the output knows whose it is
    }

    [Fact]
    public async Task Scores_from_tenant_wide_records_are_tenant_wide()
    {
        SeedUnits();
        await ActivatePackagesAsync("treasury-controls-v1.json", "compliance-aml-kyc-v1.json");

        await SubmitAndProcessAsync(new Dictionary<string, string>
        {
            ["ventureId"] = "V-001",
            ["amount"] = "500",
            ["amlRiskRating"] = "0.10",
            ["counterpartyId"] = "CP-1",
        }, unitRef: null);

        Assert.Null(_scores.Items.Single(s => s.ScoreType == "AMLRisk").UnitRef);
    }

    [Fact]
    public async Task Benchmark_breach_alerts_carry_the_scores_unit()
    {
        var benchmarks = new InMemoryBenchmarkStore();
        benchmarks.Items.Add(new ECARMF.Kernel.Domain.Analytics.Benchmark
        {
            TenantId = Tenant,
            Name = "AML cap",
            Kind = "score",
            MetricType = "AMLRisk",
            ExpectationOperator = ECARMF.Kernel.Domain.Packages.ConditionOperator.LessOrEqual,
            ExpectedValue = 0.3m,
            Severity = "Warning",
            Enabled = true,
        });
        var deviations = new InMemoryDeviationStore();
        var monitor = new ECARMF.Kernel.Application.Analytics.BenchmarkMonitorService(
            benchmarks, deviations, new InMemoryNotificationStore(), new InMemoryTaskStore(), _audit);

        await monitor.CheckScoreAsync(new ECARMF.Kernel.Domain.Scoring.ScoreRecord
        {
            TenantId = Tenant,
            SubjectType = "Counterparty",
            SubjectId = "CP-9",
            ScoreType = "AMLRisk",
            Value = 0.9m,
            UnitRef = "elgin",
        });

        var alert = deviations.Items.Single();
        Assert.Equal("elgin", alert.UnitRef); // the alarm names the location
    }

    [Fact]
    public async Task Archived_evidence_carries_the_unit_and_search_inherits_tenant_wide()
    {
        SeedUnits();
        var service = new ConnectorIngestionService(_connectors, _registries, Intake(), _library, _units);
        var loader = new PackageLoader(_packageStore, _registries, _audit);
        var manifest = LoadManifest("connector-reference-templates-v1.json");
        Assert.True((await loader.LoadAsync(Tenant, manifest)).Success);
        Assert.True((await loader.ActivateAsync(Tenant, manifest.PackageId, manifest.PackageVersion)).Success);
        await _connectors.EnsureSeedConnectorsAsync(Tenant);

        await service.IngestAsync(Tenant, SeedConnectors.ManualEntry,
            """{ "opportunityType": "RealEstateAcquisition", "title": "Oak doc", "estimatedValue": 1 }""",
            "o", unitRef: "oak-lawn");
        await service.IngestAsync(Tenant, SeedConnectors.ManualEntry,
            """{ "opportunityType": "RealEstateAcquisition", "title": "HR doc", "estimatedValue": 1 }""",
            "o"); // tenant-wide

        Assert.Contains(_library.Items, i => i.Document.UnitRef == "oak-lawn");
        Assert.Contains(_library.Items, i => i.Document.UnitRef == null);

        var oakView = await _library.SearchAsync(Tenant, null, null, null, null, 50, "oak-lawn");
        Assert.Equal(2, oakView.Count); // own evidence + tenant-wide evidence
        var elginView = await _library.SearchAsync(Tenant, null, null, null, null, 50, "elgin");
        Assert.Single(elginView); // only the tenant-wide document
    }

    // ---- the validator itself ----

    [Fact]
    public async Task UnitScope_accepts_tenant_wide_and_active_units_only()
    {
        SeedUnits();
        Assert.Null(await UnitScope.ValidateAsync(_units, Tenant, null));
        Assert.Null(await UnitScope.ValidateAsync(_units, Tenant, "oak-lawn"));
        Assert.NotNull(await UnitScope.ValidateAsync(_units, Tenant, "closed-site"));
        Assert.NotNull(await UnitScope.ValidateAsync(_units, Tenant, "ghost"));
        // tenant isolation: a unit of another tenant is invisible here
        _units.Items.Add(new OrganizationalUnit { TenantId = "other", UnitId = "foreign", Name = "F", UnitType = "Location" });
        Assert.NotNull(await UnitScope.ValidateAsync(_units, Tenant, "foreign"));
    }
}
