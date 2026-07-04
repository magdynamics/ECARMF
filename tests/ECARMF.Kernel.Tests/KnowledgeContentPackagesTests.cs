using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Transactions;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>
/// The knowledge-content program: COSO, GAAP, Reg D, and AML/KYC authored
/// purely as Knowledge Packages. These tests prove the kernel executes real
/// regulatory content with zero kernel changes — all five packages activate
/// together, and each domain's records get decided, scored, and routed by
/// metadata alone.
/// </summary>
public class KnowledgeContentPackagesTests
{
    private const string Tenant = "tenant-a";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InMemoryPackageStore _packageStore = new();
    private readonly InMemoryTransactionStore _recordStore = new();
    private readonly InMemoryOutcomeStore _outcomeStore = new();
    private readonly InMemoryScoreStore _scoreStore = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly TenantRegistryProvider _registries = new();
    private readonly InProcessKernelEventBus _bus = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryNotificationStore _notifications = new();

    private static readonly (string File, string PackageId, string Version)[] ContentPackages =
    [
        ("compliance-aml-kyc-v1.json", "ecarmf.compliance-aml-kyc", "1.0.0"),
        ("finance-gaap-controls-v1.json", "ecarmf.finance-gaap-controls", "1.0.0"),
        ("coso-internal-controls-v1.json", "ecarmf.coso-internal-controls", "1.0.0"),
        ("regd-offering-compliance-v1.json", "ecarmf.regd-offering-compliance", "1.0.0")
    ];

    private static KnowledgePackageManifest LoadManifest(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "packages", fileName)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "packages", fileName));
        var manifest = JsonSerializer.Deserialize<KnowledgePackageManifest>(json, JsonOptions);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private async Task ActivateAllAsync()
    {
        var loader = new PackageLoader(_packageStore, _registries, _audit);

        var treasury = await loader.LoadAsync(Tenant, LoadManifest("treasury-controls-v1.json"));
        Assert.True(treasury.Success, string.Join("; ", treasury.Errors));
        Assert.True((await loader.ActivateAsync(Tenant, "ecarmf.treasury-controls", "1.2.0")).Success);

        foreach (var (file, packageId, version) in ContentPackages)
        {
            var load = await loader.LoadAsync(Tenant, LoadManifest(file));
            Assert.True(load.Success, $"{packageId}: {string.Join("; ", load.Errors)}");
            var activate = await loader.ActivateAsync(Tenant, packageId, version);
            Assert.True(activate.Success, $"{packageId}: {string.Join("; ", activate.Errors)}");
        }
    }

    /// <summary>Submits a record and drains the whole event chain, including
    /// workflow execution on follow-up events.</summary>
    private async Task<Guid> SubmitAndProcessAllAsync(string recordType, Dictionary<string, string> payload)
    {
        var intake = new TransactionIntakeService(_recordStore, _bus, _registries, _audit);
        var receipt = await intake.ReceiveAsync(new TransactionSubmission(
            Tenant, recordType, "submitter@example.com", payload));
        Assert.True(receipt.EventPublished, receipt.Note);

        var workflows = new WorkflowEngine(_registries, _tasks, _notifications, _bus, _audit);
        var processor = new EventProcessor(_registries, _outcomeStore, _scoreStore, _bus, _audit,
            new ECARMF.Kernel.Application.Performance.PerformanceEvaluationService(_registries, _scoreStore, _audit),
            workflows);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var pending = 1;
        while (pending > 0)
        {
            Assert.True(await enumerator.MoveNextAsync());
            pending--;
            var result = await processor.ProcessAsync(enumerator.Current);
            if (result.Outcome is not null
                && string.Equals(result.EventName, KernelEventNames.RecordReceived, StringComparison.OrdinalIgnoreCase)
                && _registries.GetFor(Tenant).Events.IsDeclared(result.Outcome.Outcome))
            {
                pending++;
            }
        }

        return receipt.TransactionId;
    }

    private async Task<TransactionOutcome> OutcomeOfAsync(Guid transactionId) =>
        Assert.Single(await _outcomeStore.GetForTransactionsAsync(Tenant, [transactionId]));

    [Fact]
    public async Task All_content_packages_activate_together_over_the_treasury_base()
    {
        await ActivateAllAsync();

        var registries = _registries.GetFor(Tenant);
        // Each domain's vocabulary is registered without conflicts.
        Assert.True(registries.Events.IsDeclared("AMLEscalated"));
        Assert.True(registries.Events.IsDeclared("JournalHeld"));
        Assert.True(registries.Events.IsDeclared("ControlDeficiencyLogged"));
        Assert.True(registries.Events.IsDeclared("AccreditationReviewRequired"));
        Assert.True(registries.SchemaTemplates.TryGet("gaap-journal-json", out _));
        Assert.True(registries.PerformanceFrameworks.TryGet("coso-maturity-v1", out _));
        Assert.NotEmpty(registries.Workflows.GetWorkflowsForEvent("AMLEscalated"));
        Assert.NotEmpty(registries.Workflows.GetWorkflowsForEvent("ControlDeficiencyLogged"));
    }

    [Fact]
    public async Task Sanctioned_counterparty_is_rejected_before_the_generic_treasury_flag()
    {
        await ActivateAllAsync();

        var id = await SubmitAndProcessAllAsync("withdrawal", new Dictionary<string, string>
        {
            ["ventureId"] = "V-001",
            ["amount"] = "75000", // would merely be Flagged by TREASURY-R-001
            ["counterpartyId"] = "CP-9",
            ["counterpartyName"] = "Shadow Trading FZE",
            ["counterpartyJurisdiction"] = "sanctioned-region",
            ["kycStatus"] = "Verified",
            ["sanctionsHit"] = "true",
            ["amlRiskRating"] = "0.97"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("Rejected", outcome.Outcome);
        Assert.Equal("AML-R-010", outcome.RuleId);
        Assert.Contains("Sanctions screening hit", outcome.Reason);
        // The scoring rule ran before the decider: risk is recorded even on rejection.
        var risk = Assert.Single(_scoreStore.Items, s => s.ScoreType == "AMLRisk");
        Assert.Equal(0.97m, risk.Value);
        Assert.Equal("CP-9", risk.SubjectId);
    }

    [Fact]
    public async Task Unverified_counterparty_above_CDD_threshold_escalates_and_routes_to_compliance()
    {
        await ActivateAllAsync();

        var id = await SubmitAndProcessAllAsync("withdrawal", new Dictionary<string, string>
        {
            ["ventureId"] = "V-001",
            ["amount"] = "25000",
            ["counterpartyId"] = "CP-2",
            ["counterpartyName"] = "NewCo Ltd",
            ["kycStatus"] = "Pending",
            ["sanctionsHit"] = "false"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("AMLEscalated", outcome.Outcome);
        Assert.Equal("AML-R-015", outcome.RuleId);

        // AML-WF-001 executed on the follow-up event.
        var notification = Assert.Single(_notifications.Items);
        Assert.Equal("RiskComplianceOfficer", notification.Target);
        Assert.Equal("Critical", notification.Severity);
        var task = Assert.Single(_tasks.Items);
        Assert.Contains("NewCo Ltd", task.Title);
    }

    [Fact]
    public async Task Unbalanced_journal_entry_is_held_and_a_clean_one_is_approved_with_a_compliance_score()
    {
        await ActivateAllAsync();

        var unbalanced = await SubmitAndProcessAllAsync("JournalEntry", new Dictionary<string, string>
        {
            ["entryId"] = "JE-100",
            ["debitTotal"] = "5000",
            ["creditTotal"] = "4750",
            ["balanced"] = "false"
        });
        var held = await OutcomeOfAsync(unbalanced);
        Assert.Equal("JournalHeld", held.Outcome);
        Assert.Equal("GAAP-R-010", held.RuleId);
        Assert.Contains("debits 5000 vs credits 4750", held.Reason);

        var clean = await SubmitAndProcessAllAsync("JournalEntry", new Dictionary<string, string>
        {
            ["entryId"] = "JE-101",
            ["debitTotal"] = "5000",
            ["creditTotal"] = "5000",
            ["balanced"] = "true",
            ["periodStatus"] = "Open"
        });
        var approved = await OutcomeOfAsync(clean);
        Assert.Equal("Approved", approved.Outcome);
        Assert.Equal("GAAP-R-030", approved.RuleId);
        var score = Assert.Single(_scoreStore.Items, s => s.ScoreType == "ControlCompliance");
        Assert.Equal("JE-101", score.SubjectId);
    }

    [Fact]
    public async Task Material_manual_adjustment_is_flagged_for_dual_approval()
    {
        await ActivateAllAsync();

        var id = await SubmitAndProcessAllAsync("JournalEntry", new Dictionary<string, string>
        {
            ["entryId"] = "JE-200",
            ["debitTotal"] = "250000",
            ["creditTotal"] = "250000",
            ["balanced"] = "true",
            ["manualAdjustment"] = "true",
            ["amount"] = "250000",
            ["preparedBy"] = "controller@example.com"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("Flagged", outcome.Outcome);
        Assert.Equal("GAAP-R-020", outcome.RuleId);
    }

    [Fact]
    public async Task Material_weakness_logs_a_deficiency_scores_the_component_and_routes_remediation()
    {
        await ActivateAllAsync();

        var id = await SubmitAndProcessAllAsync("ControlAssessment", new Dictionary<string, string>
        {
            ["controlId"] = "CTRL-AP-01",
            ["cosoComponent"] = "ControlActivities",
            ["effectivenessRating"] = "0.35",
            ["deficiencySeverity"] = "MaterialWeakness",
            ["assessor"] = "auditor@example.com"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("ControlDeficiencyLogged", outcome.Outcome);
        Assert.Equal("COSO-R-020", outcome.RuleId);

        // Scoring rule ran first: control + component scores recorded.
        Assert.Contains(_scoreStore.Items, s => s.ScoreType == "ControlEffectiveness" && s.SubjectId == "CTRL-AP-01");
        Assert.Contains(_scoreStore.Items, s => s.ScoreType == "COSOComponentHealth" && s.SubjectId == "ControlActivities");
        // The COSO maturity KPI computed from the same record.
        Assert.Contains(_scoreStore.Items, s => s.ScoreType == "KPIActual" && s.SubjectId.Contains("ControlActivities"));

        // COSO-WF-001: auditor notified, remediation task opened.
        var notification = Assert.Single(_notifications.Items);
        Assert.Equal("Auditor", notification.Target);
        var task = Assert.Single(_tasks.Items);
        Assert.Contains("CTRL-AP-01", task.Title);
    }

    [Fact]
    public async Task RegD_506c_self_certification_is_rejected_and_verified_subscription_is_accepted()
    {
        await ActivateAllAsync();

        var selfCertified = await SubmitAndProcessAllAsync("InvestorSubscription", new Dictionary<string, string>
        {
            ["investorId"] = "INV-1",
            ["offeringId"] = "OFF-2026-A",
            ["exemptionRule"] = "506c",
            ["accreditationStatus"] = "SelfCertified",
            ["verificationMethod"] = "questionnaire",
            ["amount"] = "100000"
        });
        var rejected = await OutcomeOfAsync(selfCertified);
        Assert.Equal("Rejected", rejected.Outcome);
        Assert.Equal("REGD-R-010", rejected.RuleId);

        var verified = await SubmitAndProcessAllAsync("InvestorSubscription", new Dictionary<string, string>
        {
            ["investorId"] = "INV-2",
            ["offeringId"] = "OFF-2026-A",
            ["exemptionRule"] = "506c",
            ["accreditationStatus"] = "Verified",
            ["verificationMethod"] = "cpa-letter",
            ["amount"] = "250000"
        });
        var approved = await OutcomeOfAsync(verified);
        Assert.Equal("Approved", approved.Outcome);
        Assert.Equal("REGD-R-030", approved.RuleId);
        var score = Assert.Single(_scoreStore.Items, s => s.ScoreType == "InvestorAccreditation");
        Assert.Equal("OFF-2026-A", score.SubjectId);
    }

    [Fact]
    public async Task Pending_accreditation_waits_in_the_compliance_queue()
    {
        await ActivateAllAsync();

        var id = await SubmitAndProcessAllAsync("InvestorSubscription", new Dictionary<string, string>
        {
            ["investorId"] = "INV-3",
            ["offeringId"] = "OFF-2026-B",
            ["exemptionRule"] = "506b",
            ["accreditationStatus"] = "Pending",
            ["verificationMethod"] = "bank-statement",
            ["amount"] = "50000"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("AccreditationReviewRequired", outcome.Outcome);
        var task = Assert.Single(_tasks.Items);
        Assert.Contains("INV-3", task.Title);
    }

    [Fact]
    public async Task Ordinary_treasury_withdrawals_are_untouched_by_the_content_packages()
    {
        await ActivateAllAsync();

        // No AML fields at all: none of the content rules can match (a missing
        // payload field never matches), so the treasury control still decides.
        var id = await SubmitAndProcessAllAsync("withdrawal", new Dictionary<string, string>
        {
            ["ventureId"] = "V-001",
            ["amount"] = "60000"
        });

        var outcome = await OutcomeOfAsync(id);
        Assert.Equal("Flagged", outcome.Outcome);
        Assert.Equal("TREASURY-R-001", outcome.RuleId);
    }
}
