namespace ECARMF.Kernel.Domain.Library;

/// <summary>
/// One archived upload in the tenant's source library. Every payload that
/// enters the platform — a pasted form, a bank file, an extracted PDF, an
/// integration feed — is archived verbatim before processing and indexed by
/// its metadata, so with dozens of sources the original evidence behind any
/// record is always retrievable: what arrived, from where, from whom, when,
/// and which records it produced.
/// </summary>
public class SourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>pdf | text | json | csv — how the content should be read.</summary>
    public string MediaType { get; set; } = "text";

    /// <summary>SHA-256 of the archived content: tamper-evidence and duplicate detection.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>The connector or integration the upload came through.</summary>
    public string SourceId { get; set; } = string.Empty;

    public string SourceCategory { get; set; } = string.Empty;

    public string UploadedBy { get; set; } = string.Empty;

    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>How fields were extracted, when an extraction stage ran
    /// (regex-template | llm:model | none).</summary>
    public string? ExtractionBackend { get; set; }

    public string? SchemaTemplateId { get; set; }

    /// <summary>Records the upload produced — the lineage from evidence to outcome.</summary>
    public List<Guid> RecordIds { get; set; } = [];

    /// <summary>Free-form index metadata (searchable).</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
