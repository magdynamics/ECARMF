namespace ECARMF.Kernel.Domain.Knowledge;

/// <summary>
/// The structured data pulled from one document — the numbers behind the
/// prose, made queryable. A bank statement yields {bank, account, period,
/// depositsTotal}; a W-2 yields {employee, employer, taxYear, wages}. This is
/// what makes reconciliation possible AND trustworthy: the platform sums these
/// stored values deterministically, so "add all deposits in BOA account 123"
/// is arithmetic over real extracted numbers, never the model guessing a total.
/// </summary>
public class ExtractedDocumentData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The library document this data came from.</summary>
    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Canonical document type key (bank-statement, w2, ...).</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Organizational unit the document belongs to; null = group-wide.</summary>
    public string? UnitRef { get; set; }

    /// <summary>The primary subject the document is about — an account number,
    /// an employee name, an entity — used to filter reconciliation queries.
    /// Lower-cased for matching.</summary>
    public string? SubjectKey { get; set; }

    /// <summary>A period label the document covers (e.g. "2024", "2026-06").</summary>
    public string? Period { get; set; }

    /// <summary>Every extracted field, name → value (strings; numeric fields
    /// parse for aggregation). Includes the type's key fields.</summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    public string Backend { get; set; } = string.Empty;

    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.UtcNow;
}
