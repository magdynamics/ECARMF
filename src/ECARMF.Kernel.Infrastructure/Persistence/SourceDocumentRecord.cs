using System.Security.Cryptography;
using System.Text.Json;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class SourceDocumentRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceCategory { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public string? UnitRef { get; set; }
    public DateTimeOffset ArchivedAt { get; set; }
    public string? ExtractionBackend { get; set; }
    public string? SchemaTemplateId { get; set; }
    public string RecordIdsJson { get; set; } = "[]";
    public string MetadataJson { get; set; } = "{}";
    public byte[] Content { get; set; } = [];
}

public class EfDocumentLibrary : IDocumentLibrary
{
    private readonly ECARMFDbContext _db;

    public EfDocumentLibrary(ECARMFDbContext db) => _db = db;

    public async Task<SourceDocument> ArchiveAsync(
        SourceDocument document, byte[] content, CancellationToken ct = default)
    {
        document.Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        document.SizeBytes = content.LongLength;

        _db.SourceDocuments.Add(new SourceDocumentRecord
        {
            Id = document.Id,
            TenantId = document.TenantId,
            FileName = document.FileName,
            MediaType = document.MediaType,
            Sha256 = document.Sha256,
            SizeBytes = document.SizeBytes,
            SourceId = document.SourceId,
            SourceCategory = document.SourceCategory,
            UploadedBy = document.UploadedBy,
            UnitRef = document.UnitRef,
            ArchivedAt = document.ArchivedAt,
            ExtractionBackend = document.ExtractionBackend,
            SchemaTemplateId = document.SchemaTemplateId,
            RecordIdsJson = JsonSerializer.Serialize(document.RecordIds),
            MetadataJson = JsonSerializer.Serialize(document.Metadata),
            Content = content
        });
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public async Task<IReadOnlyList<SourceDocument>> SearchAsync(
        string tenantId, string? query, string? sourceId,
        DateTimeOffset? from, DateTimeOffset? to, int limit,
        string? unitRef = null, CancellationToken ct = default)
    {
        var documents = _db.SourceDocuments.AsNoTracking().Where(d => d.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(unitRef))
        {
            // A unit's evidence plus what applies to every unit.
            documents = documents.Where(d => d.UnitRef == unitRef || d.UnitRef == null);
        }

        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            documents = documents.Where(d => d.SourceId == sourceId);
        }
        if (from is not null)
        {
            documents = documents.Where(d => d.ArchivedAt >= from);
        }
        if (to is not null)
        {
            documents = documents.Where(d => d.ArchivedAt <= to);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            documents = documents.Where(d =>
                d.FileName.Contains(term)
                || d.SourceId.Contains(term)
                || d.SourceCategory.Contains(term)
                || (d.SchemaTemplateId != null && d.SchemaTemplateId.Contains(term))
                || d.MetadataJson.Contains(term)
                || d.Sha256 == term.ToLower()
                || d.RecordIdsJson.Contains(term));
        }

        var records = await documents
            .OrderByDescending(d => d.ArchivedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<SourceDocument?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.SourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<byte[]?> GetContentAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.SourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        return record?.Content;
    }

    public async Task SetUnitAndCategoryAsync(
        string tenantId, Guid id, string? unitRef, string category, CancellationToken ct = default)
    {
        var record = await _db.SourceDocuments.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        if (record is null) return;
        record.UnitRef = string.IsNullOrWhiteSpace(unitRef) ? null : unitRef.Trim();
        record.SourceCategory = category;
        await _db.SaveChangesAsync(ct);
    }

    private static SourceDocument ToDomain(SourceDocumentRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        FileName = record.FileName,
        MediaType = record.MediaType,
        Sha256 = record.Sha256,
        SizeBytes = record.SizeBytes,
        SourceId = record.SourceId,
        SourceCategory = record.SourceCategory,
        UploadedBy = record.UploadedBy,
        UnitRef = record.UnitRef,
        ArchivedAt = record.ArchivedAt,
        ExtractionBackend = record.ExtractionBackend,
        SchemaTemplateId = record.SchemaTemplateId,
        RecordIds = JsonSerializer.Deserialize<List<Guid>>(record.RecordIdsJson) ?? [],
        Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(record.MetadataJson) ?? []
    };
}
