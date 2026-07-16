using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Identity;
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
    string Rationale,
    string Advisor = "deterministic");

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
    private readonly ILanguageModelProvider? _llmProvider;

    // The language-model layer (Onboarding Phase 2) is optional: without it —
    // or without a platform AI credential — the deterministic profile stands
    // alone, exactly as in Phase 1.
    public OnboardingAdvisorService(IPackageCatalog catalog, ILanguageModelProvider? llmProvider = null)
    {
        _catalog = catalog;
        _llmProvider = llmProvider;
    }

    public async Task<RecommendationPack> RecommendAsync(RecommendInput input, CancellationToken ct = default)
    {
        var pack = await RecommendDeterministicAsync(input, ct);

        // Phase 2: when the PLATFORM tenant has an AI backend, let the model
        // refine the deterministic result — novel industries, better rationale,
        // extra catalog picks. Strictly advisory and strictly validated: the
        // model chooses only from the real catalog, and any failure returns
        // the deterministic pack unchanged (enrollment never depends on AI).
        if (_llmProvider is not null)
        {
            var llm = await _llmProvider.GetForTenantAsync(PlatformTenant.Id, ct);
            if (llm.IsConfigured)
            {
                pack = await RefineWithModelAsync(input, pack, llm, ct);
            }
        }

        return pack;
    }

    private async Task<RecommendationPack> RecommendDeterministicAsync(RecommendInput input, CancellationToken ct)
    {
        // Industry detection reads the business profile only. RegulatoryContext
        // deliberately stays OUT of this text: it shapes posture/PHI below, and
        // letting it in misfiles industries (e.g. a restaurant whose note says
        // "handles patient data" is not a medical-billing business).
        var text = $"{input.Industry} {input.Description} {input.Name}".ToLowerInvariant();
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

    // ── Phase 2: language-model refinement ─────────────────────────────────

    private sealed record ModelSkillPick(string? PackageId, string? Reason);

    private sealed record ModelRefinement(
        string? DetectedIndustry, string? SuggestedTier, bool? HandlesPhi,
        List<ModelSkillPick>? AddSkills, List<string>? Notes, string? Rationale);

    private static readonly JsonSerializerOptions ModelJson = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] ValidTiers = ["Standard", "Elevated", "Regulated"];

    private async Task<RecommendationPack> RefineWithModelAsync(
        RecommendInput input, RecommendationPack pack, ILanguageModelClient llm, CancellationToken ct)
    {
        try
        {
            var catalog = await _catalog.ListAsync(ct);
            var catalogLines = string.Join("\n", catalog.Take(200).Select(e => $"- {e.PackageId}: {e.Name}"));

            var system =
                "You are the tenant-onboarding profiler inside the ECARMF platform kernel. You refine a " +
                "deterministic recommendation for onboarding a new client tenant. Rules that override " +
                "everything else: (1) you advise only — an operator reviews before anything is provisioned; " +
                "(2) recommend skills ONLY from the provided catalog, by exact packageId; (3) never invent " +
                "regulations or requirements; (4) posture may only be raised for a concrete regulatory reason " +
                "you state. Respond with ONLY a JSON object (no markdown, no prose) with keys: " +
                "detectedIndustry (string), suggestedTier (Standard|Elevated|Regulated), handlesPhi (bool), " +
                "addSkills (array of {packageId, reason}, max 4, may be empty), notes (array of strings, max 3), " +
                "rationale (string, 2-3 sentences for the operator).";

            var user =
                $"Prospective tenant profile:\n" +
                $"- Name: {input.Name}\n- Industry (self-described): {input.Industry}\n" +
                $"- Size: {input.SizeBand}\n- Description: {input.Description}\n" +
                $"- Regulatory context: {input.RegulatoryContext}\n\n" +
                $"Deterministic recommendation to refine:\n{JsonSerializer.Serialize(pack, ModelJson)}\n\n" +
                $"Available skill catalog (packageId: name):\n{catalogLines}";

            var raw = await llm.CompleteAsync(system, user, ct);
            var refined = ParseRefinement(raw);
            if (refined is null)
            {
                return pack with { Notes = [.. pack.Notes, "AI refinement returned an unusable answer — showing the deterministic profile."] };
            }

            // Merge, trusting nothing blindly: tier must be a known value, PHI
            // can only be turned ON by the model (never silently off), and
            // added skills must exist in the real catalog.
            var tier = ValidTiers.FirstOrDefault(t => t.Equals(refined.SuggestedTier, StringComparison.OrdinalIgnoreCase)) ?? pack.SuggestedTier;
            var phi = pack.HandlesPhi || refined.HandlesPhi == true;
            if (phi) tier = "Regulated";

            var skills = new List<RecommendedSkill>(pack.Skills);
            var have = new HashSet<string>(skills.Select(s => s.PackageId), StringComparer.OrdinalIgnoreCase);
            foreach (var pick in (refined.AddSkills ?? []).Take(4))
            {
                if (string.IsNullOrWhiteSpace(pick.PackageId)) continue;
                var entry = catalog.FirstOrDefault(e => string.Equals(e.PackageId, pick.PackageId, StringComparison.OrdinalIgnoreCase));
                if (entry is null || !have.Add(entry.PackageId)) continue; // hallucinated or duplicate — dropped
                var (skTier, _) = SkillCatalogService.Classify(entry.PackageId, entry.Name);
                skills.Add(new RecommendedSkill(entry.PackageId, entry.Name, skTier,
                    string.IsNullOrWhiteSpace(pick.Reason) ? "Suggested by the AI profiler." : pick.Reason!.Trim(), "Medium"));
            }

            var notes = new List<string>(pack.Notes);
            foreach (var n in (refined.Notes ?? []).Take(3))
            {
                if (!string.IsNullOrWhiteSpace(n)) notes.Add(n.Trim());
            }

            return pack with
            {
                DetectedIndustry = string.IsNullOrWhiteSpace(refined.DetectedIndustry) ? pack.DetectedIndustry : refined.DetectedIndustry.Trim(),
                SuggestedTier = tier,
                HandlesPhi = phi,
                Skills = skills,
                Notes = notes,
                Rationale = string.IsNullOrWhiteSpace(refined.Rationale) ? pack.Rationale : refined.Rationale.Trim(),
                Advisor = $"ai:{llm.ModelReference}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The AI layer must never block onboarding.
            return pack with { Notes = [.. pack.Notes, $"AI refinement unavailable ({ex.Message}) — showing the deterministic profile."] };
        }
    }

    private static ModelRefinement? ParseRefinement(string raw)
    {
        var text = raw.Trim();
        // Models often wrap JSON in code fences or preamble despite instructions.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<ModelRefinement>(text[start..(end + 1)], ModelJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
