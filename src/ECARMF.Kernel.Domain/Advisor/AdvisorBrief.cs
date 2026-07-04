namespace ECARMF.Kernel.Domain.Advisor;

/// <summary>One actionable recommendation inside an advisor brief. Rationale
/// is required — AI advice without an explanation is not admissible.</summary>
public class AdvisorRecommendation
{
    public string Recommendation { get; set; } = string.Empty;

    public string Rationale { get; set; } = string.Empty;

    /// <summary>High | Medium | Low.</summary>
    public string Priority { get; set; } = "Medium";
}

/// <summary>
/// The Executive Advisor agent's output: a synthesized readout of the
/// tenant's scores, deviations, allocations, and open work, produced under
/// the agent's own AI-actor identity (provenance AIGenerated). Human
/// feedback on a brief feeds the same ModelAccuracy trust loop every other
/// AI output goes through — the advisor earns weight, it is not granted it.
/// </summary>
public class AdvisorBrief
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ExecutiveSummary { get; set; } = string.Empty;

    public List<AdvisorRecommendation> Recommendations { get; set; } = [];

    /// <summary>Which reasoning backend produced it (e.g.
    /// "advisor:claude-opus-4-8" or "advisor:deterministic-v1") — the subject
    /// the ModelAccuracy feedback is recorded against.</summary>
    public string ModelReference { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Human verdict on the brief; null until reviewed.</summary>
    public bool? FeedbackUseful { get; set; }

    public string? FeedbackBy { get; set; }

    public DateTimeOffset? FeedbackAt { get; set; }
}
