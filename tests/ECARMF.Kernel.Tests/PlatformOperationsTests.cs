using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Billing;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Integrations;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Billing;
using ECARMF.Kernel.Domain.Integrations;
using ECARMF.Kernel.Domain.Library;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Scoring;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

// ---- in-memory fakes for the platform-operations ports ----

public class InMemoryDocumentLibrary : IDocumentLibrary
{
    public List<(SourceDocument Document, byte[] Content)> Items { get; } = [];

    public Task<SourceDocument> ArchiveAsync(SourceDocument document, byte[] content, CancellationToken ct = default)
    {
        document.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        document.SizeBytes = content.LongLength;
        Items.Add((document, content));
        return Task.FromResult(document);
    }

    public Task<IReadOnlyList<SourceDocument>> SearchAsync(
        string tenantId, string? query, string? sourceId,
        DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>(
            Items.Select(i => i.Document).Where(d => d.TenantId == tenantId).Take(limit).ToList());

    public Task<SourceDocument?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.Select(i => i.Document).FirstOrDefault(d => d.TenantId == tenantId && d.Id == id));

    public Task<byte[]?> GetContentAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Document.TenantId == tenantId && i.Document.Id == id).Content);
}

public class InMemoryBenchmarkStore : IBenchmarkStore
{
    public List<Benchmark> Items { get; } = [];
    public Task<Benchmark?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(b => b.TenantId == tenantId && b.Id == id));
    public Task<IReadOnlyList<Benchmark>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Benchmark>>(Items.Where(b => b.TenantId == tenantId).ToList());
    public Task<IReadOnlyList<Benchmark>> GetEnabledAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Benchmark>>(Items.Where(b => b.TenantId == tenantId && b.Enabled).ToList());
    public Task AddAsync(Benchmark benchmark, CancellationToken ct = default) { Items.Add(benchmark); return Task.CompletedTask; }
    public Task UpdateAsync(Benchmark benchmark, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    { Items.RemoveAll(b => b.TenantId == tenantId && b.Id == id); return Task.CompletedTask; }
}

public class InMemoryIntegrationStore : IIntegrationStore
{
    public List<IntegrationDefinition> Items { get; } = [];
    public List<FeedRun> Runs { get; } = [];
    public Dictionary<string, string?> Secrets { get; } = [];

    public Task<IntegrationDefinition?> GetAsync(string tenantId, string integrationId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.TenantId == tenantId && i.IntegrationId == integrationId));
    public Task<IReadOnlyList<IntegrationDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrationDefinition>>(Items.Where(i => i.TenantId == tenantId).ToList());
    public Task AddAsync(IntegrationDefinition integration, CancellationToken ct = default)
    { Items.Add(integration); return Task.CompletedTask; }
    public Task UpdateAsync(IntegrationDefinition integration, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddRunAsync(FeedRun run, CancellationToken ct = default) { Runs.Add(run); return Task.CompletedTask; }
    public Task<IReadOnlyList<FeedRun>> GetRunsAsync(string tenantId, string? integrationId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FeedRun>>(Runs.Where(r => r.TenantId == tenantId).ToList());
    public Task<IReadOnlyList<IntegrationDefinition>> GetDueScheduledPullsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrationDefinition>>(
            Items.Where(i => i.Status == "Active" && i.Mode == "pull" && i.PullIntervalMinutes is not null
                && (i.LastFeedAt is null || i.LastFeedAt.Value.AddMinutes(i.PullIntervalMinutes.Value) <= now)).ToList());
    public Task SetAuthSecretAsync(string tenantId, string integrationId, string? secret, CancellationToken ct = default)
    { Secrets[$"{tenantId}/{integrationId}"] = secret; return Task.CompletedTask; }
    public Task<string?> GetAuthSecretAsync(string tenantId, string integrationId, CancellationToken ct = default) =>
        Task.FromResult(Secrets.TryGetValue($"{tenantId}/{integrationId}", out var s) ? s : null);
}

public class FakeFeedPuller : IFeedPuller
{
    public string? Response { get; set; }
    public string? LastUrl { get; private set; }
    public string? LastSecret { get; private set; }
    public Task<string> FetchAsync(string url, string? bearerSecret, CancellationToken ct = default)
    {
        LastUrl = url;
        LastSecret = bearerSecret;
        return Response is null
            ? throw new HttpRequestException("unreachable")
            : Task.FromResult(Response);
    }
}

/// <summary>Access keys, benchmarks, the source library, managed integration
/// feeds, and utilization billing — the platform-operations layer.</summary>
public class PlatformOperationsTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public void Access_keys_hash_deterministically_and_never_repeat()
    {
        var key1 = AccessKey.Generate();
        var key2 = AccessKey.Generate();

        Assert.StartsWith(AccessKey.Prefix, key1);
        Assert.NotEqual(key1, key2);
        Assert.Equal(AccessKey.Hash(key1), AccessKey.Hash(key1));
        Assert.NotEqual(AccessKey.Hash(key1), AccessKey.Hash(key2));
        Assert.Equal(64, AccessKey.Hash(key1).Length); // sha-256 hex
    }

    // ---- benchmarks ----

    private static Benchmark AmountCap() => new()
    {
        TenantId = Tenant,
        Name = "No single movement above 10k",
        Kind = "recordField",
        RecordType = "withdrawal",
        Field = "amount",
        ExpectationOperator = ConditionOperator.LessOrEqual,
        ExpectedValue = 10_000m,
        Severity = "Critical",
        NotifyRole = "ExecutiveOwner",
        CreateTask = true,
        CreatedBy = "owner@platform"
    };

    [Fact]
    public async Task Benchmark_breach_raises_alert_notification_task_and_audit()
    {
        var benchmarks = new InMemoryBenchmarkStore();
        var alerts = new InMemoryDeviationStore();
        var notifications = new InMemoryNotificationStore();
        var tasks = new InMemoryTaskStore();
        var audit = new InMemoryAuditLog();
        benchmarks.Items.Add(AmountCap());
        var monitor = new BenchmarkMonitorService(benchmarks, alerts, notifications, tasks, audit);

        await monitor.CheckRecordAsync(Tenant, "withdrawal",
            new Dictionary<string, string> { ["amount"] = "25000" }, Guid.NewGuid());

        var alert = Assert.Single(alerts.Items);
        Assert.Equal("Benchmark", alert.ExpectedValueSource);
        Assert.Equal("Critical", alert.Severity);
        Assert.Equal(25000m, alert.ActualValue);
        var notification = Assert.Single(notifications.Items);
        Assert.Equal("ExecutiveOwner", notification.Target);
        Assert.Single(tasks.Items);
        Assert.Contains(audit.Items, a => a.Category == AuditCategories.BenchmarkBreached);
    }

    [Fact]
    public async Task Benchmark_that_holds_stays_silent()
    {
        var benchmarks = new InMemoryBenchmarkStore();
        var alerts = new InMemoryDeviationStore();
        var notifications = new InMemoryNotificationStore();
        benchmarks.Items.Add(AmountCap());
        var monitor = new BenchmarkMonitorService(
            benchmarks, alerts, notifications, new InMemoryTaskStore(), new InMemoryAuditLog());

        await monitor.CheckRecordAsync(Tenant, "withdrawal",
            new Dictionary<string, string> { ["amount"] = "9000" }, Guid.NewGuid());

        Assert.Empty(alerts.Items);
        Assert.Empty(notifications.Items);
    }

    [Fact]
    public async Task Score_benchmark_watches_the_score_fabric()
    {
        var benchmarks = new InMemoryBenchmarkStore();
        benchmarks.Items.Add(new Benchmark
        {
            TenantId = Tenant,
            Name = "GP% must stay at or above 25%",
            Kind = "score",
            MetricType = "GPPercent",
            ExpectationOperator = ConditionOperator.GreaterOrEqual,
            ExpectedValue = 0.25m,
            Severity = "Warning",
            NotifyRole = "RiskComplianceOfficer",
            CreatedBy = "owner@platform"
        });
        var alerts = new InMemoryDeviationStore();
        var monitor = new BenchmarkMonitorService(
            benchmarks, alerts, new InMemoryNotificationStore(), new InMemoryTaskStore(), new InMemoryAuditLog());

        await monitor.CheckScoreAsync(new ScoreRecord
        {
            TenantId = Tenant, SubjectType = "Company", SubjectId = "acme",
            ScoreType = "GPPercent", Value = 0.18m
        });

        var alert = Assert.Single(alerts.Items);
        Assert.Equal(0.18m, alert.ActualValue);
        Assert.Equal(0.25m, alert.ExpectedValue);
    }

    // ---- source library via ingestion ----

    [Fact]
    public async Task Every_connector_payload_is_archived_and_indexed()
    {
        var records = new InMemoryTransactionStore();
        var registries = new TenantRegistryProvider();
        registries.GetFor(Tenant).SchemaTemplates.Register(new SchemaTemplateDeclaration
        {
            TemplateId = "t-json",
            SourceFormat = "json",
            TargetEntityType = "Opportunity",
            FieldMappings = [new FieldMapping { RawField = "title", TargetField = "title", Required = true }]
        }, "pkg.lib", "1.0.0");
        var connectors = new InMemoryConnectorStore();
        await connectors.AddAsync(Tenant, new ConnectorDefinition(
            "conn-1", "Conn", "Manual", "manual", "t-json", 0.9m, Provenance.HumanEntered, "Active"));
        var library = new InMemoryDocumentLibrary();
        var audit = new InMemoryAuditLog();
        var intake = new TransactionIntakeService(records, new InProcessKernelEventBus(), registries, audit);
        var service = new ConnectorIngestionService(connectors, registries, intake, library);

        var accepted = await service.IngestAsync(Tenant, "conn-1", """{ "title": "Deal A" }""", "owner@platform");
        var rejected = await service.IngestAsync(Tenant, "conn-1", """{ "notTitle": 1 }""", "owner@platform");

        Assert.True(accepted.Success);
        Assert.False(rejected.Success);
        Assert.Equal(2, library.Items.Count); // rejected evidence is still evidence
        var acceptedDoc = library.Items[0].Document;
        Assert.Equal("conn-1", acceptedDoc.SourceId);
        Assert.Equal(accepted.RecordIds, acceptedDoc.RecordIds);
        Assert.Equal("True", acceptedDoc.Metadata["accepted"]);
        Assert.NotEmpty(acceptedDoc.Sha256);
        Assert.Equal("False", library.Items[1].Document.Metadata["accepted"]);
    }

    // ---- integrations ----

    private static (IntegrationFeedService Service, InMemoryIntegrationStore Store, InMemoryTransactionStore Records, FakeFeedPuller Puller)
        CreateIntegrationFixture()
    {
        var records = new InMemoryTransactionStore();
        var registries = new TenantRegistryProvider();
        registries.GetFor(Tenant).SchemaTemplates.Register(new SchemaTemplateDeclaration
        {
            TemplateId = "pos-json",
            SourceFormat = "json",
            TargetEntityType = "PosSale",
            FieldMappings = [new FieldMapping { RawField = "total", TargetField = "amount", Required = true }]
        }, "pkg.pos", "1.0.0");
        var connectors = new InMemoryConnectorStore();
        connectors.AddAsync(Tenant, new ConnectorDefinition(
            "pos-conn", "POS", "OperationalSystem", "push", "pos-json", 0.9m,
            Provenance.ExternalSystemVerified, "Active")).GetAwaiter().GetResult();

        var intake = new TransactionIntakeService(records, new InProcessKernelEventBus(), registries, new InMemoryAuditLog());
        var ingestion = new ConnectorIngestionService(connectors, registries, intake);
        var store = new InMemoryIntegrationStore();
        var puller = new FakeFeedPuller();
        var service = new IntegrationFeedService(store, ingestion, puller, new InMemoryAuditLog());
        return (service, store, records, puller);
    }

    [Fact]
    public async Task Pushed_feed_flows_through_the_connector_and_records_a_run()
    {
        var (service, store, records, _) = CreateIntegrationFixture();
        store.Items.Add(new IntegrationDefinition
        {
            TenantId = Tenant, IntegrationId = "pos-main", Name = "Store POS",
            ApplicationType = "POS", ConnectorId = "pos-conn", Mode = "push", CreatedBy = "admin@platform"
        });

        var run = await service.PushAsync(Tenant, "pos-main", """{ "total": 129.50 }""", "pos-system");

        Assert.True(run.Success, run.Error);
        Assert.Equal(1, run.RecordsIngested);
        Assert.Single(records.Items);
        Assert.Single(store.Runs);
        Assert.Equal("Succeeded", store.Items[0].LastFeedStatus);
    }

    [Fact]
    public async Task Paused_integration_rejects_feeds_but_still_records_the_attempt()
    {
        var (service, store, records, _) = CreateIntegrationFixture();
        store.Items.Add(new IntegrationDefinition
        {
            TenantId = Tenant, IntegrationId = "pos-main", Name = "Store POS",
            ApplicationType = "POS", ConnectorId = "pos-conn", Mode = "push",
            Status = "Paused", CreatedBy = "admin@platform"
        });

        var run = await service.PushAsync(Tenant, "pos-main", """{ "total": 10 }""", "pos-system");

        Assert.False(run.Success);
        Assert.Contains("Paused", run.Error);
        Assert.Empty(records.Items);
        Assert.Single(store.Runs);
    }

    [Fact]
    public async Task Pull_feed_fetches_with_the_stored_secret_and_ingests()
    {
        var (service, store, records, puller) = CreateIntegrationFixture();
        store.Items.Add(new IntegrationDefinition
        {
            TenantId = Tenant, IntegrationId = "acct-main", Name = "Accounting",
            ApplicationType = "Accounting", ConnectorId = "pos-conn", Mode = "pull",
            PullUrl = "https://accounting.example.com/export", CreatedBy = "admin@platform"
        });
        store.Secrets[$"{Tenant}/acct-main"] = "secret-token";
        puller.Response = """{ "total": 999 }""";

        var run = await service.PullAsync(Tenant, "acct-main", "admin@platform");

        Assert.True(run.Success, run.Error);
        Assert.Equal("https://accounting.example.com/export", puller.LastUrl);
        Assert.Equal("secret-token", puller.LastSecret);
        Assert.Single(records.Items);
    }

    [Fact]
    public async Task Unreachable_pull_endpoint_records_a_failed_run_with_health()
    {
        var (service, store, _, puller) = CreateIntegrationFixture();
        store.Items.Add(new IntegrationDefinition
        {
            TenantId = Tenant, IntegrationId = "acct-main", Name = "Accounting",
            ApplicationType = "Accounting", ConnectorId = "pos-conn", Mode = "pull",
            PullUrl = "https://down.example.com/export", CreatedBy = "admin@platform"
        });
        puller.Response = null; // throws

        var run = await service.PullAsync(Tenant, "acct-main", "admin@platform");

        Assert.False(run.Success);
        Assert.Contains("unreachable", run.Error);
        Assert.Equal("Failed", store.Items[0].LastFeedStatus);
    }

    // ---- billing ----

    private sealed class FixedMeter : IUsageMeter
    {
        public Task<UsageSummary> MeasureAsync(
            string tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct = default) =>
            Task.FromResult(new UsageSummary(tenantId, periodStart, periodEnd,
                RecordsProcessed: 1200, DocumentsArchived: 40, StorageBytes: 5_000_000,
                AiCalls: 12, FeedRuns: 60, ActiveUsers: 3));
    }

    private sealed class SinglePlanStore : IBillingPlanStore
    {
        private readonly BillingPlan _plan = new()
        {
            PlanId = "standard", Name = "Standard", BaseMonthlyFee = 500m,
            PricePerRecord = 0.05m, PricePerDocumentArchived = 0.10m,
            PricePerAiCall = 0.50m, PricePerFeedRun = 0.25m, PricePerActiveUser = 25m
        };
        public Task<BillingPlan?> GetAsync(string planId, CancellationToken ct = default) =>
            Task.FromResult<BillingPlan?>(planId == "standard" ? _plan : null);
        public Task<IReadOnlyList<BillingPlan>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BillingPlan>>([_plan]);
        public Task AddAsync(BillingPlan plan, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureDefaultPlanAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ListStatementStore : IBillingStatementStore
    {
        public List<BillingStatement> Items { get; } = [];
        public Task AddAsync(BillingStatement statement, CancellationToken ct = default)
        { Items.Add(statement); return Task.CompletedTask; }
        public Task<IReadOnlyList<BillingStatement>> GetForTenantAsync(string tenantId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BillingStatement>>(Items.Where(s => s.TenantId == tenantId).ToList());
    }

    [Fact]
    public async Task Statement_charges_utilization_times_plan_rates_line_by_line()
    {
        var statements = new ListStatementStore();
        var audit = new InMemoryAuditLog();
        var billing = new BillingService(new FixedMeter(), new SinglePlanStore(), statements, audit);

        var statement = await billing.GenerateStatementAsync(
            Tenant, "standard",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            "admin@platform");

        // 500 base + 1200*0.05 + 40*0.10 + 12*0.50 + 60*0.25 + 3*25 = 500+60+4+6+15+75
        Assert.Equal(660m, statement.Total);
        Assert.Equal(6, statement.Lines.Count);
        var recordsLine = Assert.Single(statement.Lines, l => l.Metric == "RecordsProcessed");
        Assert.Equal(60m, recordsLine.Amount);
        Assert.Single(statements.Items);
        Assert.Contains(audit.Items, a => a.Category == AuditCategories.BillingStatementGenerated);
    }

    [Fact]
    public async Task Records_query_filters_by_metadata_and_pages()
    {
        var store = new InMemoryTransactionStore();
        for (var i = 0; i < 30; i++)
        {
            await store.AppendAsync(new Domain.Transactions.Transaction
            {
                TenantId = Tenant,
                TransactionType = i % 2 == 0 ? "JournalEntry" : "withdrawal",
                SubmittedBy = "owner@platform",
                Payload = new Dictionary<string, string> { ["entryId"] = $"JE-{i}" },
                ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
        }

        var (page1, total) = await store.QueryAsync(new TransactionQuery(
            Tenant, RecordType: "JournalEntry", Take: 10));
        Assert.Equal(15, total);
        Assert.Equal(10, page1.Count);
        Assert.All(page1, t => Assert.Equal("JournalEntry", t.TransactionType));

        var (searched, searchTotal) = await store.QueryAsync(new TransactionQuery(Tenant, Text: "JE-7"));
        Assert.Equal(1, searchTotal);
        Assert.Equal("JE-7", searched.Single().Payload["entryId"]);

        Assert.Equal(["JournalEntry", "withdrawal"], await store.GetRecordTypesAsync(Tenant));
    }
}
