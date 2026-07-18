using ECARMF.Kernel.Application.Agents;
using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>MagCPA kernel extensions: person-level unit-accruing compliance
/// (CPE), the effective-dated Knowledge Reference Library, and the enforced
/// output disclaimer — all first-class patterns, not tenant hacks.</summary>
public class MagCpaKernelTests
{
    private const string Tenant = "magcpa";

    // ---- Person-level compliance -------------------------------------

    [Fact]
    public async Task Renewing_a_unit_accruing_obligation_resets_the_next_cycle_to_zero()
    {
        var renewals = new InMemoryRenewalStore();
        var monitor = new RenewalMonitorService(
            renewals, new InMemoryDeviationStore(), new InMemoryNotificationStore(),
            new InMemoryTaskStore(), new InMemoryAuditLog());
        var cpe = new RenewalCommitment
        {
            TenantId = Tenant,
            Name = "CPE - jane.cpa",
            Category = "CPE",
            SubjectType = "user",
            SubjectId = "jane.cpa@magcpa.example",
            DueDate = DateTimeOffset.UtcNow.AddMonths(6),
            RecurrenceMonths = 36, // Illinois licensing period
            RequiredUnits = 120,
            CompletedUnits = 120,
            UnitLabel = "CPE hours",
            NotifyRole = "RiskComplianceOfficer",
            CreatedBy = "admin"
        };
        renewals.Items.Add(cpe);

        var renewed = await monitor.MarkRenewedAsync(Tenant, cpe.Id, "admin", CancellationToken.None);

        Assert.NotNull(renewed);
        Assert.Equal(0, renewed!.CompletedUnits);          // the new period starts from zero
        Assert.Equal(120, renewed.RequiredUnits);          // the requirement itself persists
        Assert.Equal(RenewalStatuses.Active, renewed.Status);
    }

    // ---- Knowledge assets (Batch 2 R8: supersedes the reference library) ----

    private static KnowledgeAsset IrsPub(string year, DateTimeOffset from, DateTimeOffset? to) => new()
    {
        AssetId = $"irs-pub-334-{year}",
        DocKey = "irs-pub-334",
        Title = $"IRS Publication 334 (Tax Guide for Small Business), {year} edition",
        AssetType = "ReferenceManual",
        DocType = "IRSGuideline",
        Issuer = "IRS",
        Jurisdiction = "Federal",
        EffectiveFrom = from,
        EffectiveTo = to,
        ContentText = $"{year} rules text"
    };

    [Fact]
    public void Effective_dated_retrieval_never_returns_a_superseded_year()
    {
        var registry = new KnowledgeAssetRegistry();
        registry.Register(IrsPub("2025",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)), "ecarmf.cpa-reference", "1.0.0");
        registry.Register(IrsPub("2026",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null), "ecarmf.cpa-reference", "1.0.0");

        var during2025 = registry.GetEffective(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), docKey: "irs-pub-334");
        var during2026 = registry.GetEffective(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero), docKey: "irs-pub-334");
        var during2027 = registry.GetEffective(new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero), docKey: "irs-pub-334");

        Assert.Equal("irs-pub-334-2025", Assert.Single(during2025).Declaration.AssetId);
        Assert.Equal("irs-pub-334-2026", Assert.Single(during2026).Declaration.AssetId);
        // Open-ended EffectiveTo stays current until a successor bounds it.
        Assert.Equal("irs-pub-334-2026", Assert.Single(during2027).Declaration.AssetId);
    }

    [Fact]
    public void Validator_requires_effective_dates_on_knowledge_assets()
    {
        var manifest = new KnowledgePackageManifest
        {
            PackageId = "p", Name = "p", PackageVersion = "1.0.0",
            KnowledgeAssets =
            [
                new KnowledgeAsset { AssetId = "undated", DocKey = "k", Title = "T", DocType = "GAAP" },
                new KnowledgeAsset
                {
                    AssetId = "inverted", DocKey = "k2", Title = "T2", DocType = "GAAP",
                    EffectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    EffectiveTo = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
                },
                new KnowledgeAsset
                {
                    AssetId = "bad-edge", DocKey = "k3", Title = "T3", DocType = "GAAP",
                    EffectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    Relationships = [new KnowledgeAssetRelationship { RelatedAssetId = "", RelationshipType = "supersedes" }]
                }
            ]
        };

        var errors = ManifestValidator.Validate(manifest, new EventRegistry());

        Assert.Contains(errors, e => e.Contains("undated") && e.Contains("EffectiveFrom"));
        Assert.Contains(errors, e => e.Contains("inverted") && e.Contains("EffectiveTo"));
        Assert.Contains(errors, e => e.Contains("bad-edge") && e.Contains("RelatedAssetId"));
    }

    [Fact]
    public void References_is_a_valid_agent_context_source()
    {
        var manifest = new KnowledgePackageManifest
        {
            PackageId = "p", Name = "p", PackageVersion = "1.0.0",
            Agents = [new AgentDeclaration { AgentId = "a", Name = "A", Persona = "P", ContextSources = ["references"] }]
        };

        Assert.Empty(ManifestValidator.Validate(manifest, new EventRegistry()));
    }

    // ---- Enforced output disclaimer + reference grounding --------------

    [Fact]
    public async Task Disclaimer_is_appended_by_the_kernel_and_references_ground_only_effective_versions()
    {
        var registries = new TenantRegistryProvider();
        registries.GetFor(Tenant).KnowledgeAssets.Register(IrsPub("2025",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)), "ecarmf.cpa-reference", "1.1.0");
        registries.GetFor(Tenant).KnowledgeAssets.Register(IrsPub("2026",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null), "ecarmf.cpa-reference", "1.1.0");
        registries.GetFor(Tenant).Agents.Register(new AgentDeclaration
        {
            AgentId = "tax-compliance-agent",
            Name = "Tax Preparation & Compliance Agent",
            Persona = "You compare client returns to IRS benchmark ratios.",
            ContextSources = ["references"],
            OutputDisclaimer = "Statistical risk indicator only — not a tax determination. A licensed CPA at MagCPA decides."
        }, "ecarmf.ai-cpa-firm", "1.0.0");

        var llm = new FakeLanguageModelClient { IsConfigured = true, Response = "The ratio deviates from the IRS segment median." };
        var scores = new InMemoryScoreStore();
        var audit = new InMemoryAuditLog();
        var service = new AgentConsultService(
            registries, new FakeLanguageModelProvider(llm), new InMemoryAgentInteractionStore(),
            new InMemoryUserStore(), new AILearningFeedbackService(scores, audit), audit,
            scores, new InMemoryDeviationStore(), new InMemoryBenchmarkStore(), new InMemoryTaskStore(),
            new InMemoryCapitalFlowStore(), new InMemoryDocumentLibrary(), new InMemoryTransactionStore(),
            new InMemoryReferenceSourceStore());

        var (success, error, interaction) = await service.AskAsync(
            Tenant, "tax-compliance-agent", "Is this return an audit risk?", "partner@magcpa.example");

        Assert.True(success, error);
        // The disclaimer is part of the stored answer — the output layer
        // enforces it; neither the model nor the UI can drop it.
        Assert.EndsWith("A licensed CPA at MagCPA decides.", interaction!.Answer);
        Assert.Contains("Statistical risk indicator only", interaction.Answer);
        // Grounding context contains only the currently effective 2026 text.
        Assert.Contains("2026 rules text", llm.LastUserPrompt);
        Assert.DoesNotContain("2025 rules text", llm.LastUserPrompt);
    }
}
