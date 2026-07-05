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
}
