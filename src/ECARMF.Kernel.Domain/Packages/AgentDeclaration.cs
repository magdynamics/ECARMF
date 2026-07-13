namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// Declares a specialized AI agent as package content — an "IRS guide", an
/// "AML compliance guide", a "portfolio analyst" — the same way packages
/// declare rules and KPIs. The persona carries the domain expertise; the
/// context sources say which tenant data the agent may read; the kernel
/// supplies the guardrails (advisory-only, grounded, trust-tracked) and the
/// tenant's own AI credential. Shipping a smarter agent is a new package
/// version, never a kernel change.
/// </summary>
public class AgentDeclaration
{
    public string AgentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The agent's domain persona and knowledge — the package-authored
    /// system prompt (e.g. IRS guidance grounded in the package's reference data).</summary>
    public string Persona { get; set; } = string.Empty;

    /// <summary>Tenant data the agent receives as grounding context. Valid:
    /// scores | deviations | benchmarks | tasks | allocations | library |
    /// references | records:{RecordType}. Anything not declared is not
    /// shown to the agent.</summary>
    public List<string> ContextSources { get; set; } = [];

    /// <summary>Example questions surfaced in the UI.</summary>
    public List<string> SampleQuestions { get; set; } = [];

    /// <summary>Liability framing APPENDED TO EVERY ANSWER at the output
    /// layer (MagCPA Requirement 5/7): e.g. "statistical risk indicator,
    /// not a tax determination — a licensed CPA decides". Enforced by the
    /// consult service, not left as a persona suggestion the model could
    /// drop.</summary>
    public string? OutputDisclaimer { get; set; }

    // ---- Identity block (TCEL P2.2) ----
    // The T9-002–006 agent-spec structure, restored as first-class fields so
    // authors fill them in rather than reconstruct them from memory. All
    // optional (existing packages omit them); a missing Owner is a warning,
    // never a load error. The "Allowed" side is already ContextSources +
    // Persona, so only the Prohibited side is added here.

    /// <summary>Team or role accountable for this agent.</summary>
    public string? Owner { get; set; }

    /// <summary>Who independently validates the agent's outputs (a segregation
    /// role distinct from the Owner).</summary>
    public string? IndependentValidator { get; set; }

    /// <summary>Open risk-tier tag (do NOT enum it — same open-tag philosophy
    /// as ScoreRecord.riskType): e.g. Low, Elevated, Regulated.</summary>
    public string? RiskTier { get; set; }

    /// <summary>Explicit "must not" list for the agent, beyond the kernel's
    /// non-negotiable guardrails — e.g. "never quote a specific denial code",
    /// "never advise on individual compensation".</summary>
    public List<string> Prohibited { get; set; } = [];
}
