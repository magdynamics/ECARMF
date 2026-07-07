namespace ECARMF.Kernel.Domain.Packages;

/// <summary>
/// AI-driven extraction template (AI Financial Analyst, step 1) — the
/// sibling of SchemaTemplate for UNSTRUCTURED input. Where a SchemaTemplate
/// maps known fields deterministically, an AIExtractionTemplate asks the
/// tenant's model to find the declared fields in a scanned/printed document
/// and to report a per-field CONFIDENCE alongside every value. Everything
/// extracted is provenance AIGenerated; values below the review threshold
/// are gated behind human review before anything downstream may see them.
/// Printed documents only in this phase — handwriting is a separate
/// confidence regime and ships as its own validated phase.
/// </summary>
public class AIExtractionTemplateDeclaration
{
    public string TemplateId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>What the extraction produces (e.g. "FinancialStatement").</summary>
    public string TargetType { get; set; } = "FinancialStatement";

    /// <summary>Document kinds this template accepts. Phase 1: "Printed"
    /// only; "Handwritten" is rejected until its own validation phase.</summary>
    public List<string> DocumentKinds { get; set; } = ["Printed"];

    /// <summary>Any extracted value whose confidence falls below this gates
    /// the WHOLE statement behind human review (hard requirement — a
    /// misread figure must never silently reach a score or capital flow).</summary>
    public decimal ReviewThreshold { get; set; } = 0.85m;

    /// <summary>The canonical line items the model must locate. Field names
    /// are open strings, so GAAP, non-GAAP, and tax-return layouts are all
    /// template content — never schema changes.</summary>
    public List<ExtractionFieldDeclaration> Fields { get; set; } = [];
}

public class ExtractionFieldDeclaration
{
    /// <summary>Canonical label (e.g. "currentAssets") — the key ratio
    /// formulas reference.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the model should look for, in plain language
    /// ("Total current assets, however the statement labels them").</summary>
    public string Description { get; set; } = string.Empty;

    public string DataType { get; set; } = "number";

    public bool Required { get; set; }
}
