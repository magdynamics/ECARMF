namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// One entry in the Knowledge Reference Library (MagCPA Requirement 5): a
/// retrievable regulatory/professional reference (IRS guideline, GAAP/GAAS
/// section, IDOR bulletin) with an EXPLICIT effective-date range. Reference
/// documents are versioned through the package mechanism like everything
/// else — tax law changes annually, so a 2027 question must never retrieve
/// 2025 rules. Distinct from PolicyDocument-style enforceable rules: these
/// are knowledge the agents cite, not conditions the kernel executes.
/// </summary>
public class ReferenceDocumentDeclaration
{
    /// <summary>Unique per registered version, e.g. "irs-pub-334-2026".</summary>
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>Stable identity across annual versions, e.g. "irs-pub-334".
    /// Retrieval by DocKey + as-of date selects the version whose effective
    /// range covers the date.</summary>
    public string DocKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Open classification: IRSGuideline, GAAP, GAAS,
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

    /// <summary>The retrievable reference text (or an excerpt/pointer when
    /// the full text is licensed, e.g. the FASB codification).</summary>
    public string? ContentText { get; set; }
}
