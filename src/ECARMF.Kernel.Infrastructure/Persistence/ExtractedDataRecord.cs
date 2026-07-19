using System.Text.Json;
using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class ExtractedDataRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? UnitRef { get; set; }
    public string? SubjectKey { get; set; }
    public string? Period { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public string Backend { get; set; } = string.Empty;
    public DateTimeOffset ExtractedAt { get; set; }
}

public class EfExtractedDataStore : IExtractedDataStore
{
    private readonly ECARMFDbContext _db;

    public EfExtractedDataStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(ExtractedDocumentData data, CancellationToken ct = default)
    {
        _db.ExtractedData.Add(new ExtractedDataRecord
        {
            Id = data.Id, TenantId = data.TenantId, DocumentId = data.DocumentId, FileName = data.FileName,
            DocumentType = data.DocumentType, UnitRef = data.UnitRef, SubjectKey = data.SubjectKey,
            Period = data.Period, FieldsJson = JsonSerializer.Serialize(data.Fields), Backend = data.Backend,
            ExtractedAt = data.ExtractedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExtractedDocumentData>> GetByTypeAsync(string tenantId, string documentType, CancellationToken ct = default)
    {
        var records = await _db.ExtractedData.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.DocumentType == documentType)
            .OrderByDescending(x => x.ExtractedAt).Take(5000).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static ExtractedDocumentData ToDomain(ExtractedDataRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, DocumentId = r.DocumentId, FileName = r.FileName,
        DocumentType = r.DocumentType, UnitRef = r.UnitRef, SubjectKey = r.SubjectKey, Period = r.Period,
        Fields = JsonSerializer.Deserialize<Dictionary<string, string>>(r.FieldsJson) ?? [],
        Backend = r.Backend, ExtractedAt = r.ExtractedAt
    };
}
