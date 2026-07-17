using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Analysis;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Performance;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Analysis;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryFinancialStatementStore : IFinancialStatementStore
{
    public List<FinancialStatement> Items { get; } = [];
    public Task<FinancialStatement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.TenantId == tenantId && s.Id == id));
    public Task<IReadOnlyList<FinancialStatement>> GetAllAsync(string tenantId, string? status = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FinancialStatement>>(Items
            .Where(s => s.TenantId == tenantId && (status is null || s.Status == status)).ToList());
    public Task AddAsync(FinancialStatement statement, CancellationToken ct = default)
    { Items.Add(statement); return Task.CompletedTask; }
    public Task UpdateAsync(FinancialStatement statement, CancellationToken ct = default) => Task.CompletedTask;
}

public class CapturingIntake : ITransactionIntakeService
{
    public List<TransactionSubmission> Received { get; } = [];
    public Task<TransactionReceipt> ReceiveAsync(TransactionSubmission submission, CancellationToken ct = default)
    {
        Received.Add(submission);
        return Task.FromResult(new TransactionReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow, true, null));
    }
}

/// <summary>AI Financial Analyst: confidence-gated extraction, the human
/// review gate, and ratio analysis riding the existing KPI engine — a
/// misread figure must never silently reach a score or a capital flow.</summary>
public class FinancialAnalystTests
{
    private const string Tenant = "any-tenant";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TenantRegistryProvider _registries = new();
    private readonly FakeLanguageModelClient _llm = new() { IsConfigured = true };
    private readonly InMemoryFinancialStatementStore _statements = new();
    private readonly CapturingIntake _intake = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly FinancialStatementService _service;

    public FinancialAnalystTests()
    {
        _service = new FinancialStatementService(
            _registries, new FakeLanguageModelProvider(_llm), _statements, _intake, _audit);
        _registries.GetFor(Tenant).AiExtractionTemplates.Register(new AIExtractionTemplateDeclaration
        {
            TemplateId = "financial-statement-printed-v1",
            Name = "Printed statement",
            TargetType = "FinancialStatement",
            DocumentKinds = ["Printed"],
            ReviewThreshold = 0.85m,
            Fields =
            [
                new ExtractionFieldDeclaration { Name = "revenue", Description = "Total revenue", Required = true },
                new ExtractionFieldDeclaration { Name = "cogs", Description = "Cost of goods sold" },
                new ExtractionFieldDeclaration { Name = "currentAssets", Description = "Current assets" },
                new ExtractionFieldDeclaration { Name = "currentLiabilities", Description = "Current liabilities" }
            ]
        }, "ecarmf.ai-financial-analyst", "1.0.0");
    }

    private static string ModelJson(params (string Name, decimal Value, decimal Confidence)[] fields) =>
        JsonSerializer.Serialize(new
        {
            statementType = "IncomeStatement",
            fields = fields.Select(f => new { name = f.Name, value = f.Value, confidence = f.Confidence, sourceText = $"{f.Name} line" })
        });

    [Fact]
    public async Task Low_confidence_extraction_is_gated_and_feeds_NOTHING_downstream()
    {
        _llm.Response = ModelJson(("revenue", 500000m, 0.95m), ("cogs", 210000m, 0.55m)); // cogs is a shaky read

        var outcome = await _service.ExtractAsync(
            Tenant, "financial-statement-printed-v1", "Printed", "scan.pdf",
            "REVENUE .... 500,000 / COGS .... 210,000", "client-a", "FY2025", "analyst@tenant");

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(FinancialStatementStatuses.PendingReview, outcome.Statement!.Status);
        Assert.Single(outcome.Statement.LowConfidenceItems); // cogs flagged
        Assert.All(outcome.Statement.LineItems, l => Assert.Equal("AIGenerated", l.Provenance));
        Assert.Empty(_intake.Received); // THE hard requirement: nothing downstream
    }

    [Fact]
    public async Task Fully_confident_extraction_auto_approves_and_releases_for_analysis()
    {
        _llm.Response = ModelJson(("revenue", 500000m, 0.97m), ("cogs", 210000m, 0.93m));

        var outcome = await _service.ExtractAsync(
            Tenant, "financial-statement-printed-v1", "Printed", "scan.pdf",
            "doc text", "client-a", "FY2025", "analyst@tenant");

        Assert.Equal(FinancialStatementStatuses.Approved, outcome.Statement!.Status);
        var record = Assert.Single(_intake.Received);
        Assert.Equal(FinancialStatementService.AnalyzedRecordType, record.TransactionType);
        Assert.Equal("500000", record.Payload["revenue"]);
        Assert.Equal("client-a", record.Payload["subjectEntity"]);
    }

    [Fact]
    public async Task Review_gate_requires_a_human_and_corrections_before_release()
    {
        _llm.Response = ModelJson(("revenue", 500000m, 0.95m), ("cogs", 210000m, 0.4m));
        var outcome = await _service.ExtractAsync(
            Tenant, "financial-statement-printed-v1", "Printed", "scan.pdf",
            "doc", "client-a", "FY2025", "analyst@tenant");
        var id = outcome.Statement!.Id;

        // An AI actor can never be the reviewer.
        var ai = new User { Identifier = "system:flywheel", IsSystemActor = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReviewAsync(Tenant, id, ai, true, [], null));

        // A human cannot approve while a flagged value is uncorrected.
        var human = new User { Identifier = "cfo@tenant", IsSystemActor = false };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ReviewAsync(Tenant, id, human, true, [], null));

        // Correcting the flagged value makes it HumanEntered at full
        // confidence, and approval releases the statement.
        var reviewed = await _service.ReviewAsync(Tenant, id, human, true,
            [new LineCorrection("cogs", 215000m)], "Verified against the source scan.");

        Assert.Equal(FinancialStatementStatuses.Approved, reviewed.Status);
        var cogs = reviewed.LineItems.Single(l => l.Label == "cogs");
        Assert.Equal(215000m, cogs.Value);
        Assert.Equal(1.0m, cogs.ConfidenceScore);
        Assert.Equal("HumanEntered", cogs.Provenance);
        var record = Assert.Single(_intake.Received);
        Assert.Equal("215000", record.Payload["cogs"]);
    }

    [Fact]
    public async Task Handwritten_documents_are_refused_in_this_phase()
    {
        var outcome = await _service.ExtractAsync(
            Tenant, "financial-statement-printed-v1", "Handwritten", "scan.pdf",
            "doc", "client-a", "FY2025", "analyst@tenant");

        Assert.False(outcome.Success);
        Assert.Contains("Handwritten", outcome.Error);
        Assert.Empty(_statements.Items);
    }

    [Fact]
    public void Package_manifest_validates_and_declares_the_analyst_surface()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "ai-financial-analyst-v1.json")))
        {
            directory = directory.Parent;
        }
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", "ai-financial-analyst-v1.json"));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions)!;

        Assert.Empty(ManifestValidator.Validate(manifest, new EventRegistry()));
        var template = Assert.Single(manifest.AiExtractionTemplates);
        Assert.Equal(["Printed"], template.DocumentKinds); // handwritten deferred
        Assert.Equal(0.85m, template.ReviewThreshold);
        var agent = Assert.Single(manifest.Agents);
        Assert.Contains("NOT a lending, credit, or investment determination", agent.OutputDisclaimer);
    }

    [Fact]
    public async Task Ratios_ride_the_existing_KPI_engine_with_FinancialRisk_tags()
    {
        // Register the REAL framework from the package file and evaluate the
        // released record through the same engine as every tenant KPI.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", "ai-financial-analyst-v1.json")))
        {
            directory = directory.Parent;
        }
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(
            File.ReadAllText(Path.Combine(directory!.FullName, "packages", "ai-financial-analyst-v1.json")), JsonOptions)!;
        _registries.GetFor(Tenant).PerformanceFrameworks.Register(
            manifest.PerformanceFrameworks.Single(), manifest.PackageId, manifest.PackageVersion);

        var scores = new InMemoryScoreStore();
        var evaluator = new PerformanceEvaluationService(_registries, scores, _audit);
        await evaluator.EvaluateAsync(new KernelEvent(Tenant, "RecordReceived", Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["recordType"] = FinancialStatementService.AnalyzedRecordType,
                ["subjectEntity"] = "client-a",
                ["currentAssets"] = "300000",
                ["inventory"] = "80000",
                ["currentLiabilities"] = "250000",
                ["totalAssets"] = "900000",
                ["totalDebt"] = "500000",
                ["totalEquity"] = "180000",
                ["revenue"] = "1200000",
                ["cogs"] = "700000",
                ["operatingExpenses"] = "350000",
                ["netIncome"] = "95000",
                ["debtService"] = "120000"
            }, DateTimeOffset.UtcNow));

        var actuals = scores.Items.Where(s => s.ScoreType == "KPIActual").ToList();
        Assert.Equal(1.2m, Math.Round(actuals.Single(s => s.SubjectId == "current-ratio@client-a").Value, 2));
        Assert.Equal("FinancialRisk", actuals.Single(s => s.SubjectId == "current-ratio@client-a").RiskType);
        Assert.Equal(2.78m, Math.Round(actuals.Single(s => s.SubjectId == "debt-to-equity@client-a").Value, 2));
        Assert.Equal(1.25m, Math.Round(actuals.Single(s => s.SubjectId == "statement-dscr@client-a").Value, 2));
        Assert.Null(actuals.Single(s => s.SubjectId == "gross-margin@client-a").RiskType); // margins inform, not risk-tagged
    }

    [Fact]
    public async Task Statement_unit_flows_onto_the_released_record()
    {
        _llm.Response = ModelJson(("revenue", 500000m, 0.97m), ("cogs", 210000m, 0.93m));

        var outcome = await _service.ExtractAsync(
            Tenant, "financial-statement-printed-v1", "Printed", "scan.pdf",
            "doc text", "client-a", "FY2025", "analyst@tenant", unitRef: "oak-lawn");

        Assert.Equal("oak-lawn", outcome.Statement!.UnitRef);
        var record = Assert.Single(_intake.Received);
        Assert.Equal("oak-lawn", record.UnitRef); // ratio scores will be the unit's
    }
}
