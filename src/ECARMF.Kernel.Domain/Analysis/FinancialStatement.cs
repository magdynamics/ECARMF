namespace ECARMF.Kernel.Domain.Analysis;

/// <summary>
/// A financial statement captured for analysis (AI Financial Analyst,
/// step 2). Structured statements arrive via the normal connectors; this
/// entity exists for the UNSTRUCTURED path: a scanned/printed document the
/// AI extracted, every value carrying its confidence and provenance, the
/// original document retained in the library for audit. Line items are
/// open label/value pairs — GAAP, non-GAAP, and tax-return formats fit
/// without schema changes. Nothing here feeds ratios, scores, or capital
/// flows until Status is Approved.
/// </summary>
public class FinancialStatement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Open string: BalanceSheet, IncomeStatement, CashFlow,
    /// TaxReturn — or any future type.</summary>
    public string StatementType { get; set; } = string.Empty;

    /// <summary>Whose statement (org unit slug, client code — open).</summary>
    public string SubjectEntity { get; set; } = string.Empty;

    /// <summary>Validated organizational unit the statement belongs to;
    /// null = tenant-wide. Unlike SubjectEntity (free text), this is a real
    /// unit and flows onto the released record and its ratio scores.</summary>
    public string? UnitRef { get; set; }

    /// <summary>Statement period label (e.g. "FY2025", "2026-Q2").</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Deterministic | AIExtraction.</summary>
    public string ExtractionMethod { get; set; } = ExtractionMethods.AIExtraction;

    /// <summary>The extraction template that produced this statement.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Library id of the original upload, retained for audit.</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>The threshold in force when this statement was extracted.</summary>
    public decimal ReviewThreshold { get; set; }

    /// <summary>PendingReview | Approved | Rejected.</summary>
    public string Status { get; set; } = FinancialStatementStatuses.PendingReview;

    public List<StatementLineItem> LineItems { get; set; } = [];

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewComment { get; set; }

    /// <summary>Set when the approved statement was released into the
    /// analysis pipeline (record intake → ratios → risk flags).</summary>
    public DateTimeOffset? AnalyzedAt { get; set; }

    /// <summary>Fields whose confidence fell below the threshold — the
    /// reason this statement needs a human before it counts.</summary>
    public IEnumerable<StatementLineItem> LowConfidenceItems =>
        LineItems.Where(l => l.ConfidenceScore < ReviewThreshold);
}

public class StatementLineItem
{
    public string Label { get; set; } = string.Empty;

    public decimal Value { get; set; }

    /// <summary>0..1 — the model's own certainty for THIS value. Corrected
    /// values are set to 1.0 with provenance HumanEntered.</summary>
    public decimal ConfidenceScore { get; set; }

    /// <summary>AIGenerated for extracted values; HumanEntered after a
    /// reviewer corrects one.</summary>
    public string Provenance { get; set; } = "AIGenerated";

    /// <summary>The text the model says it read the value from — shown to
    /// the reviewer next to the flagged figure.</summary>
    public string? SourceText { get; set; }
}

public static class FinancialStatementStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ExtractionMethods
{
    public const string Deterministic = "Deterministic";
    public const string AIExtraction = "AIExtraction";
}
