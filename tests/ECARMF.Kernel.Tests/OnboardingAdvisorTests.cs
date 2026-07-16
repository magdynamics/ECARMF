using ECARMF.Kernel.Application.Onboarding;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

/// <summary>The onboarding recommender drives real provisioning decisions —
/// posture, PHI, and which skills get installed — so its industry mapping and
/// overrides must be exact.</summary>
public class OnboardingAdvisorTests
{
    private static (OnboardingAdvisorService Advisor, StubPackageCatalog Catalog) Build()
    {
        var catalog = new StubPackageCatalog();
        // A representative slice of the live library: industry skills + the
        // universal governance baseline the advisor must always include.
        catalog.Entries.AddRange(
        [
            StubPackageCatalog.Entry("ecarmf.ai-dental", "AI Dental"),
            StubPackageCatalog.Entry("ecarmf.ai-dental-risk-register", "Dental Practice Risk Register"),
            StubPackageCatalog.Entry("ecarmf.ai-restaurant", "AI Restaurant"),
            StubPackageCatalog.Entry("ecarmf.ai-tcel-trading", "TCEL Trading & Portfolio Management"),
            StubPackageCatalog.Entry("ecarmf.ai-risk-register", "Enterprise Risk Register"),
            StubPackageCatalog.Entry("ecarmf.ai-autonomous-orchestration", "Autonomous Orchestration & Remediation"),
            StubPackageCatalog.Entry("ecarmf.ai-financial-continuity", "Financial Continuity & Liquidity"),
        ]);
        return (new OnboardingAdvisorService(catalog), catalog);
    }

    [Fact]
    public async Task Dental_profile_gets_regulated_phi_posture_and_dental_skills()
    {
        var (advisor, _) = Build();
        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Bright Smile Dental", "Dental practice", null, "multi-location dental group", null));

        Assert.Equal("Dental practice", pack.DetectedIndustry);
        Assert.Equal("Regulated", pack.SuggestedTier);
        Assert.True(pack.HandlesPhi);
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-dental");
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-dental-risk-register");
        Assert.Contains(pack.Notes, n => n.Contains("PHI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Every_recommendation_includes_the_universal_governance_baseline()
    {
        var (advisor, _) = Build();
        foreach (var industry in new[] { "Dental practice", "Restaurant", "Widgets manufacturing" })
        {
            var pack = await advisor.RecommendAsync(new RecommendInput("Co", industry, null, null, null));
            Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-risk-register");
            Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-autonomous-orchestration");
            Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-financial-continuity");
        }
    }

    [Fact]
    public async Task Unknown_industry_falls_back_to_baseline_only_with_a_note()
    {
        var (advisor, _) = Build();
        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Mystery Co", "Widgets manufacturing", null, null, null));

        Assert.Equal("General business", pack.DetectedIndustry);
        Assert.Equal("Standard", pack.SuggestedTier);
        Assert.False(pack.HandlesPhi);
        Assert.Equal(3, pack.Skills.Count); // baseline only
        Assert.Contains(pack.Notes, n => n.Contains("wasn't recognized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Regulatory_context_overrides_a_standard_industry_to_regulated_phi()
    {
        var (advisor, _) = Build();
        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Green Fork Bistro", "Restaurant", null, null, "handles HIPAA patient data"));

        Assert.Equal("Restaurant / food service", pack.DetectedIndustry);
        Assert.True(pack.HandlesPhi);
        Assert.Equal("Regulated", pack.SuggestedTier);
    }

    [Fact]
    public async Task Industry_skill_count_is_capped_at_six()
    {
        var catalog = new StubPackageCatalog();
        for (var i = 0; i < 10; i++)
            catalog.Entries.Add(StubPackageCatalog.Entry($"ecarmf.dental-extra-{i}", $"Dental Extra {i}"));
        var advisor = new OnboardingAdvisorService(catalog);

        var pack = await advisor.RecommendAsync(new RecommendInput("Clinic", "Dental practice", null, null, null));
        Assert.True(pack.Skills.Count(s => s.Tier != "Core") <= 6,
            $"expected at most 6 industry skills, got {pack.Skills.Count(s => s.Tier != "Core")}");
    }

    [Fact]
    public async Task Trading_profile_matches_the_tcel_skill_set_at_regulated_without_phi()
    {
        var (advisor, _) = Build();
        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Apex Capital", "Asset management", null, "multi-manager trading and treasury, securities", null));

        Assert.Equal("Trading / treasury / asset management", pack.DetectedIndustry);
        Assert.Equal("Regulated", pack.SuggestedTier);
        Assert.False(pack.HandlesPhi);
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-tcel-trading");
    }

    // ── Phase 2: language-model refinement ──────────────────────────────────

    private static (OnboardingAdvisorService Advisor, FakeLanguageModelClient Llm) BuildWithAi()
    {
        var catalog = new StubPackageCatalog();
        catalog.Entries.AddRange(
        [
            StubPackageCatalog.Entry("ecarmf.ai-restaurant", "AI Restaurant"),
            StubPackageCatalog.Entry("ecarmf.ai-financial-analyst", "AI Financial Analyst"),
            StubPackageCatalog.Entry("ecarmf.ai-risk-register", "Enterprise Risk Register"),
            StubPackageCatalog.Entry("ecarmf.ai-autonomous-orchestration", "Autonomous Orchestration & Remediation"),
            StubPackageCatalog.Entry("ecarmf.ai-financial-continuity", "Financial Continuity & Liquidity"),
        ]);
        var llm = new FakeLanguageModelClient { IsConfigured = true };
        return (new OnboardingAdvisorService(catalog, new FakeLanguageModelProvider(llm)), llm);
    }

    [Fact]
    public async Task Ai_refinement_merges_validated_fields_and_marks_the_advisor()
    {
        var (advisor, llm) = BuildWithAi();
        llm.Response = """
            {"detectedIndustry":"Ghost-kitchen restaurant group","suggestedTier":"Elevated","handlesPhi":false,
             "addSkills":[{"packageId":"ecarmf.ai-financial-analyst","reason":"Multi-location P&L extraction."}],
             "notes":["Consider per-location units."],"rationale":"Delivery-first restaurant group; elevated for multi-entity cash handling."}
            """;

        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Cloud Bites", "Restaurant", null, "delivery-only ghost kitchens", null));

        Assert.Equal("ai:fake-model", pack.Advisor);
        Assert.Equal("Ghost-kitchen restaurant group", pack.DetectedIndustry);
        Assert.Equal("Elevated", pack.SuggestedTier);
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-financial-analyst");
        Assert.Contains(pack.Notes, n => n.Contains("per-location"));
        Assert.Contains("Delivery-first", pack.Rationale);
        // Deterministic picks survive the merge.
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-restaurant");
        Assert.Contains(pack.Skills, s => s.PackageId == "ecarmf.ai-risk-register");
    }

    [Fact]
    public async Task Ai_cannot_add_hallucinated_skills_or_invalid_tiers_or_unset_phi()
    {
        var (advisor, llm) = BuildWithAi();
        llm.Response = """
            {"detectedIndustry":"Dental","suggestedTier":"Ultra","handlesPhi":false,
             "addSkills":[{"packageId":"ecarmf.made-up-skill","reason":"x"}],"notes":[],"rationale":"r"}
            """;

        // Deterministic layer says PHI + Regulated (dental); the model tries to
        // lower both and to add a skill that doesn't exist.
        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Bright Smile", "Dental practice", null, null, null));

        Assert.True(pack.HandlesPhi);                       // PHI can never be switched off by the model
        Assert.Equal("Regulated", pack.SuggestedTier);      // invalid tier ignored, PHI keeps it Regulated
        Assert.DoesNotContain(pack.Skills, s => s.PackageId == "ecarmf.made-up-skill");
    }

    [Fact]
    public async Task Ai_garbage_falls_back_to_the_deterministic_pack_with_a_note()
    {
        var (advisor, llm) = BuildWithAi();
        llm.Response = "Sorry, I cannot produce JSON today.";

        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Green Fork", "Restaurant", null, null, null));

        Assert.Equal("deterministic", pack.Advisor);
        Assert.Equal("Restaurant / food service", pack.DetectedIndustry);
        Assert.Contains(pack.Notes, n => n.Contains("unusable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ai_backend_failure_never_blocks_the_recommendation()
    {
        var (advisor, llm) = BuildWithAi();
        llm.Throws = new HttpRequestException("backend down");

        var pack = await advisor.RecommendAsync(new RecommendInput(
            "Green Fork", "Restaurant", null, null, null));

        Assert.Equal("deterministic", pack.Advisor);
        Assert.Contains(pack.Notes, n => n.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unconfigured_ai_leaves_the_deterministic_pack_untouched()
    {
        var catalog = new StubPackageCatalog();
        catalog.Entries.Add(StubPackageCatalog.Entry("ecarmf.ai-risk-register", "Enterprise Risk Register"));
        var llm = new FakeLanguageModelClient { IsConfigured = false };
        var advisor = new OnboardingAdvisorService(catalog, new FakeLanguageModelProvider(llm));

        var pack = await advisor.RecommendAsync(new RecommendInput("Co", "Restaurant", null, null, null));

        Assert.Equal("deterministic", pack.Advisor);
        Assert.Null(llm.LastUserPrompt); // never called
    }
}
