using System.Text.Json;
using ECARMF.Kernel.Application.Analysis;
using ECARMF.Kernel.Domain.Analysis;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class FinancialStatementRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string StatementType { get; set; } = string.Empty;
    public string SubjectEntity { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string ExtractionMethod { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public decimal ReviewThreshold { get; set; }
    public string Status { get; set; } = string.Empty;
    public string LineItemsJson { get; set; } = "[]";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset? AnalyzedAt { get; set; }
}

public class EfFinancialStatementStore : IFinancialStatementStore
{
    private readonly ECARMFDbContext _db;

    public EfFinancialStatementStore(ECARMFDbContext db) => _db = db;

    public async Task<FinancialStatement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.FinancialStatements.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<FinancialStatement>> GetAllAsync(
        string tenantId, string? status = null, CancellationToken ct = default)
    {
        var query = _db.FinancialStatements.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Status == status);
        }
        var records = await query.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(FinancialStatement statement, CancellationToken ct = default)
    {
        _db.FinancialStatements.Add(ToRecord(statement));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(FinancialStatement statement, CancellationToken ct = default)
    {
        var record = await _db.FinancialStatements.FirstAsync(
            s => s.TenantId == statement.TenantId && s.Id == statement.Id, ct);
        record.Status = statement.Status;
        record.LineItemsJson = JsonSerializer.Serialize(statement.LineItems);
        record.ReviewedBy = statement.ReviewedBy;
        record.ReviewedAt = statement.ReviewedAt;
        record.ReviewComment = statement.ReviewComment;
        record.AnalyzedAt = statement.AnalyzedAt;
        await _db.SaveChangesAsync(ct);
    }

    private static FinancialStatementRecord ToRecord(FinancialStatement s) => new()
    {
        Id = s.Id, TenantId = s.TenantId, StatementType = s.StatementType,
        SubjectEntity = s.SubjectEntity, Period = s.Period,
        ExtractionMethod = s.ExtractionMethod, TemplateId = s.TemplateId,
        SourceDocumentId = s.SourceDocumentId, ReviewThreshold = s.ReviewThreshold,
        Status = s.Status, LineItemsJson = JsonSerializer.Serialize(s.LineItems),
        CreatedBy = s.CreatedBy, CreatedAt = s.CreatedAt,
        ReviewedBy = s.ReviewedBy, ReviewedAt = s.ReviewedAt,
        ReviewComment = s.ReviewComment, AnalyzedAt = s.AnalyzedAt
    };

    private static FinancialStatement ToDomain(FinancialStatementRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, StatementType = r.StatementType,
        SubjectEntity = r.SubjectEntity, Period = r.Period,
        ExtractionMethod = r.ExtractionMethod, TemplateId = r.TemplateId,
        SourceDocumentId = r.SourceDocumentId, ReviewThreshold = r.ReviewThreshold,
        Status = r.Status,
        LineItems = string.IsNullOrWhiteSpace(r.LineItemsJson)
            ? []
            : JsonSerializer.Deserialize<List<StatementLineItem>>(r.LineItemsJson) ?? [],
        CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
        ReviewedBy = r.ReviewedBy, ReviewedAt = r.ReviewedAt,
        ReviewComment = r.ReviewComment, AnalyzedAt = r.AnalyzedAt
    };
}
