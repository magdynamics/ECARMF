using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Application.Library;

/// <summary>
/// The tenant's archive-and-index of source uploads. Archiving is verbatim
/// (content preserved byte-for-byte with its hash); indexing is by the
/// document's metadata. The library is append-only: archived evidence is
/// never modified or deleted.
/// </summary>
public interface IDocumentLibrary
{
    /// <summary>Archives content with its metadata; computes hash and size.</summary>
    Task<SourceDocument> ArchiveAsync(SourceDocument document, byte[] content, CancellationToken ct = default);

    /// <summary>Metadata search: free-text over file name, source, category,
    /// template, and metadata; optionally narrowed by source and time range.</summary>
    /// <param name="unitRef">Narrow to one unit's evidence; tenant-wide
    /// documents (UnitRef null) are always included.</param>
    Task<IReadOnlyList<SourceDocument>> SearchAsync(
        string tenantId, string? query, string? sourceId,
        DateTimeOffset? from, DateTimeOffset? to, int limit,
        string? unitRef = null, CancellationToken ct = default);

    Task<SourceDocument?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);

    /// <summary>The archived original, byte-for-byte.</summary>
    Task<byte[]?> GetContentAsync(string tenantId, Guid id, CancellationToken ct = default);
}
