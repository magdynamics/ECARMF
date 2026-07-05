namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// One entry in the tenant's knowledge base (Batch 2, Refinement 8 —
/// supersedes the MagCPA-era KnowledgeReferenceLibrary). A retrievable
/// knowledge item with an EXPLICIT effective-date range: a regulatory
/// reference (IRS guideline, GAAP section), a policy document, an SOP,
/// training material, meeting notes — the assetType is an open string.
/// MagCPA's simple versioned-citation case is a KnowledgeAsset with no
/// relationships; MagDynamics' knowledge graph is the SAME entity with
/// relationships populated and semantic search enabled. One entity, not
/// two. Versioned through the package mechanism like everything else —
/// tax law changes annually, so a 2027 question must never retrieve 2025
/// rules.
/// </summary>
public class KnowledgeAsset
{
    /// <summary>Unique per registered version, e.g. "irs-pub-334-2026".</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>Stable identity across versions, e.g. "irs-pub-334".
    /// Retrieval by DocKey + as-of date selects the version whose effective
    /// range covers the date.</summary>
    public string DocKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Open kind: ReferenceManual, PolicyDocument, SOP,
    /// TrainingMaterial, MeetingNotes — or any future type.</summary>
    public string AssetType { get; set; } = "ReferenceManual";

    /// <summary>Open domain classification: IRSGuideline, GAAP, GAAS,
    /// AuditGuideline, StateRevenue — any future type.</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>Issuing body (IRS, FASB, AICPA, IDOR...).</summary>
    public string? Issuer { get; set; }

    /// <summary>Federal, Illinois, ... — null when jurisdiction-neutral.</summary>
    public string? Jurisdiction { get; set; }

    /// <summary>The date this version takes effect. Required — an undated
    /// rule is unusable the moment the next year's version arrives.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Last day this version applies; null = still current until a
    /// successor version says otherwise.</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    public string? Summary { get; set; }

    /// <summary>The retrievable text (or an excerpt when the full text is
    /// licensed, e.g. the FASB codification).</summary>
    public string? ContentText { get; set; }

    /// <summary>Pointer to an archived document in the library when the
    /// content lives as a file rather than inline text.</summary>
    public string? DocumentReference { get; set; }

    /// <summary>Knowledge-graph edges to other assets (by AssetId or
    /// DocKey): supersedes, interprets, implements, relatedTo — open.</summary>
    public List<KnowledgeAssetRelationship> Relationships { get; set; } = [];

    /// <summary>Opt-in flag for semantic-search indexing (MagDynamics);
    /// plain effective-dated retrieval works regardless.</summary>
    public bool SemanticSearchEnabled { get; set; }
}

public class KnowledgeAssetRelationship
{
    public string RelatedAssetId { get; set; } = string.Empty;

    /// <summary>Open string: supersedes | interprets | implements |
    /// relatedTo | future types.</summary>
    public string RelationshipType { get; set; } = "relatedTo";
}
