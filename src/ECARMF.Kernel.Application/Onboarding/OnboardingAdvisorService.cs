using ECARMF.Kernel.Application.Packages;

namespace ECARMF.Kernel.Application.Onboarding;

public sealed record RecommendInput(
    string Name, string? Industry, string? SizeBand, string? Description, string? RegulatoryContext);

public sealed record RecommendedSkill(
    string PackageId, string DisplayName, string Tier, string Reason, string Confidence);

public sealed record RecommendationPack(
    string DetectedIndustry,
    string SuggestedTier,
    bool HandlesPhi,
    string? SuggestedSegment,
    string? SuggestedAccent,
    IReadOnlyList<RecommendedSkill> Skills,
    IReadOnlyList<string> Notes,
    string Rationale);

public interface IOnboardingAdvisor
{
    Task<RecommendationPack> RecommendAsync(RecommendInput input, CancellationToken ct = default);
}

/// <summary>
/// Profiles a prospective tenant's industry and recommends the skills, posture,
/// and branding to onboard it with. The core is deterministic (an industry map
/// matched against the live skill library), so it works with no AI configured;
/// a language-model layer can later refine the rationale and handle novel
/// industries. Advisory only — the operator reviews and adjusts before
/// provisioning.
/// </summary>
public class OnboardingAdvisorService : IOnboardingAdvisor
{
    // Skills every tenant should get: the governance/risk baseline.
    private static readonly string[] UniversalSkills =
        ["ecarmf.ai-risk-register", "ecarmf.ai-autonomous-orchestration", "ecarmf.ai-financial-continuity"];

    private sealed record Industry(
        string Name, string[] Keywords, string[] SkillKeywords, string Tier, bool Phi, string Accent);

    // Ordered — the first industry whose keywords appear wins.
    private static readonly Industry[] Industries =
    [
        new("Dental practice", ["dental", "orthodon", "teeth", "eaglesoft"], ["dental"], "Regulated", true, "#2fbf9f"),
        new("Medical billing / RCM", ["medical", "clinic", "hospital", "rcm", "revenue cycle", "patient", "phi", "claim"], ["rcm", "tenant10", "medical", "claim"], "Regulated", true, "#e06a8b"),
        new("Accounting / CPA firm", ["cpa", "accounting", "accountant", "bookkeep", "tax", "audit firm"], ["cpa", "financial-analyst", "accounting"], "Elevated", false, "#5aa9e6"),
        new("Trading / treasury / asset management", ["trading", "treasury", "asset manage", "investment", "fund", "capital", "broker", "securities", "portfolio", "hedge"], ["tcel", "trading", "treasury", "banking", "capital", "manager"], "Regulated", false, "#c69749"),
        new("Fractional / tokenized assets", ["fractional", "token", "crypto", "custody", "digital asset"], ["fractional", "altera", "capital-markets"], "Regulated", false, "#8b5cf6"),
        new("Spa / wellness", ["spa", "wellness", "salon", "beauty", "massage", "aesthetic"], ["spa", "oxygen", "wellness"], "Elevated", true, "#34d399"),
        new("Restaurant / food service", ["restaurant", "food", "dining", "cafe", "kitchen", "hospitality", "fish", "grocery"], ["restaurant", "food", "fish"], "Standard", false, "#f59e0b"),
        new("Real estate / property", ["real estate", "realty", "property", "brokerage", "lease"], ["realty", "harbor", "property"], "Elevated", false, "#38bdf8"),
        new("Projects / grants / nonprofit", ["project", "grant", "nonprofit", "foundation", "program", "milestone"], ["rosetta", "project", "funding"], "Standard", false, "#a78bfa"),
    ];

    private readonly IPackageCatalog _catalog;

    public OnboardingAdvisorService(IPackageCatalog catalog) => _catalog = catalog;

    public async Task<RecommendationPack> RecommendAsync(RecommendInput input, CancellationToken ct = default)
    {
        var text = $"{input.Industry} {input.Description} {input.Name} {input.RegulatoryContext}".ToLowerInvariant();
        var match = Industries.FirstOrDefault(i => i.Keywords.Any(k => text.Contains(k)));
        var confident = match is not null;
        var industry = match ?? new Industry("General business", [], [], "Standard", false, "#5aa9e6");

        // Regulatory context can override the posture/PHI hint either way.
        var reg = (input.RegulatoryContext ?? "").ToLowerInvariant();
        var phi = industry.Phi || reg.Contains("phi") || reg.Contains("hipaa") || reg.Contains("health");
        var tier = industry.Tier;
        if (reg.Contains("securities") || reg.Contains("regulated") || phi) tier = "Regulated";
        else if (reg.Contains("elevated") || reg.Contains("sensitive")) tier = "Elevated";

        var catalog = await _catalog.ListAsync(ct);

        var skills = new List<RecommendedSkill>();
        var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Industry-specific skills matched against the live library (capped).
        foreach (var e in catalog)
        {
            if (skills.Count(s => s.Tier != SkillTiers.Core) >= 6) break;
            var hay = $"{e.PackageId} {e.Name}".ToLowerInvariant();
            if (industry.SkillKeywords.Any(k => hay.Contains(k)) && picked.Add(e.PackageId))
            {
                var (skTier, _) = SkillCatalogService.Classify(e.PackageId, e.Name);
                skills.Add(new RecommendedSkill(e.PackageId, e.Name, skTier,
                    $"Matches the {industry.Name.ToLowerInvariant()} profile.", confident ? "High" : "Medium"));
            }
        }

        // Universal governance/risk baseline for every tenant.
        foreach (var pid in UniversalSkills)
        {
            var e = catalog.FirstOrDefault(x => string.Equals(x.PackageId, pid, StringComparison.OrdinalIgnoreCase));
            if (e is not null && picked.Add(pid))
                skills.Add(new RecommendedSkill(e.PackageId, e.Name, SkillTiers.Core,
                    "Governance & risk baseline recommended for every tenant.", "High"));
        }

        var notes = new List<string>();
        if (!confident) notes.Add("Industry wasn't recognized from the profile — start with the universal baseline and add skills manually, or refine the description.");
        if (phi) notes.Add("Handles PHI: PHI masking will be on and an access key will be required (Regulated).");
        if (tier == "Regulated") notes.Add("Regulated posture: this tenant will require an access key (header identity is refused).");
        if (skills.Count(s => s.Tier != SkillTiers.Core) == 0 && confident)
            notes.Add($"No {industry.Name.ToLowerInvariant()} skills are in the library yet — the baseline still applies.");

        var rationale = confident
            ? $"Profiled as {industry.Name}. Recommending {skills.Count} skill(s): industry-fit plus the universal governance/risk baseline, at a {tier} posture."
            : $"Couldn't confidently place the industry, so recommending the universal governance/risk baseline at a {tier} posture. Adjust before provisioning.";

        return new RecommendationPack(
            industry.Name, tier, phi, input.Industry, industry.Accent, skills, notes, rationale);
    }
}
