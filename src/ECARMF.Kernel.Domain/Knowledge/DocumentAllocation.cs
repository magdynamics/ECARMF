namespace ECARMF.Kernel.Domain.Knowledge;

/// <summary>
/// One document's AI-recommended allocation to a business unit, awaiting a
/// human decision. Bulk mixed uploads (e.g. a folder of 1,000 documents for a
/// whole multi-entity group) are read by the model, which recommends which
/// unit each belongs to and why; a person confirms or reassigns before the
/// document is filed. AI recommends, humans decide — the platform doctrine,
/// applied to document routing.
/// </summary>
public class DocumentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The archived library document this allocation is about.</summary>
    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>The unit the AI recommends (a real unit slug, or null when it
    /// could not confidently place the document — tenant-wide/unassigned).</summary>
    public string? RecommendedUnitRef { get; set; }

    public string? RecommendedUnitName { get; set; }

    /// <summary>What kind of document the AI thinks it is (invoice, lease,
    /// tax return, license, EOB, ...). Free text.</summary>
    public string? DocumentType { get; set; }

    /// <summary>The model's 0..1 confidence in the unit recommendation.</summary>
    public decimal Confidence { get; set; }

    /// <summary>Why the AI placed it there — the human reads this to decide.</summary>
    public string? Reasoning { get; set; }

    /// <summary>Pending | Confirmed | Reassigned.</summary>
    public string Status { get; set; } = DocumentAllocationStatuses.Pending;

    /// <summary>The unit a human actually filed it under (set on decision).</summary>
    public string? DecidedUnitRef { get; set; }

    public string? DecidedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DecidedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}

public static class DocumentAllocationStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";   // human accepted the AI's unit
    public const string Reassigned = "Reassigned"; // human chose a different unit
}
