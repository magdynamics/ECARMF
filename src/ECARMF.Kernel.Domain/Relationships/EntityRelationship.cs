namespace ECARMF.Kernel.Domain.Relationships;

/// <summary>
/// A standalone directed edge between any two entities (Batch 3, Refinement
/// 13). Generalizes KnowledgeAsset's embedded relationship-edge concept
/// (Batch 2, Refinement 8) into a first-class entity usable by ANY subject
/// type — a ScoreRecord correlating with another ScoreRecord, an
/// OrganizationalUnit depending on an ITAsset, a risk driving another risk.
/// This is what lets a dependency chain (e.g. coder accuracy → denials → AR
/// aging → cash flow → churn) be represented as DATA the engine reads, rather
/// than logic hardcoded per tenant. The knowledge graph's own edges stay on
/// KnowledgeAsset; this entity does NOT replace them — it covers every other
/// subject type.
/// </summary>
public class EntityRelationship
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Entity type the edge starts at (e.g. "ScoreRecord",
    /// "OrganizationalUnit", "ITAsset"). Open string.</summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>Identity of the subject entity within its type.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Entity type the edge points at. Open string.</summary>
    public string RelatedType { get; set; } = string.Empty;

    /// <summary>Identity of the related entity within its type.</summary>
    public string RelatedId { get; set; } = string.Empty;

    /// <summary>Open relationship kind: Correlates | CausesIncreaseIn |
    /// CausesDecreaseIn | DependsOn | RollsUpInto — or any future type. Never
    /// a closed enum; the meaning is package/tenant-defined.</summary>
    public string RelationshipType { get; set; } = "Correlates";

    /// <summary>Optional correlation coefficient or weight. For a
    /// CompositeHealth rollup (Refinement 14) this is the child score's
    /// weight in the weighted aggregation; null when the edge carries no
    /// magnitude.</summary>
    public decimal? Strength { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class RelationshipTypes
{
    public const string Correlates = "Correlates";
    public const string CausesIncreaseIn = "CausesIncreaseIn";
    public const string CausesDecreaseIn = "CausesDecreaseIn";
    public const string DependsOn = "DependsOn";

    /// <summary>Marks a child whose ScoreRecord rolls up into a parent
    /// CompositeHealth score (Refinement 14); Strength is its weight.</summary>
    public const string RollsUpInto = "RollsUpInto";

    /// <summary>UI suggestions only — the field is an open string.</summary>
    public static readonly string[] All =
        { Correlates, CausesIncreaseIn, CausesDecreaseIn, DependsOn, RollsUpInto };
}
