using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Domain.Audit;

namespace ECARMF.Kernel.Application.MagAudit.Documents;

public enum DocumentImportState
{
    Inventoried,
    Uploading,
    Quarantined,
    Rejected,
    DuplicateReview,
    Extracted,
    ReviewRequired,
    Accepted
}

public sealed record DocumentImportCandidate(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string? DeclaredClientId = null,
    string? DeclaredTaxYear = null,
    string? DeclaredDocumentType = null);

public sealed record DocumentClassification(
    string DocumentType,
    string? TaxYear,
    string? TaxpayerId,
    double Confidence,
    IReadOnlyList<string> Reasons);

public sealed record MalwareScanResult(bool IsClean, string Engine, string EngineVersion, string Detail);

public sealed class DocumentImportItem
{
    public Guid ItemId { get; init; } = Guid.NewGuid();
    public required Guid BatchId { get; init; }
    public required string TenantId { get; init; }
    public required string CaseId { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public string? DeclaredClientId { get; init; }
    public string? DeclaredTaxYear { get; init; }
    public string? DeclaredDocumentType { get; init; }
    public DocumentImportState State { get; set; } = DocumentImportState.Inventoried;
    public string? EvidenceLocation { get; set; }
    public string? ExtractedText { get; set; }
    public DocumentClassification? Classification { get; set; }
    public MalwareScanResult? MalwareScan { get; set; }
    public Guid? DuplicateOfItemId { get; set; }
    public List<string> Errors { get; } = [];
}

public sealed class DocumentImportBatch
{
    public Guid BatchId { get; init; } = Guid.NewGuid();
    public required string TenantId { get; init; }
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<DocumentImportItem> Items { get; } = [];
}

public interface IMagAuditDocumentImportStore
{
    Task AddBatchAsync(DocumentImportBatch batch, CancellationToken ct = default);
    Task<DocumentImportBatch?> GetBatchAsync(string tenantId, Guid batchId, CancellationToken ct = default);
    Task SaveItemAsync(DocumentImportItem item, CancellationToken ct = default);
    Task<DocumentImportItem?> FindAcceptedByHashAsync(string tenantId, string caseId, string sha256, CancellationToken ct = default);
}

public interface IMagAuditEvidenceStore
{
    Task<string> PutQuarantinedAsync(string tenantId, string caseId, Guid itemId, string fileName, Stream content, CancellationToken ct = default);
    Task MarkAcceptedAsync(string tenantId, string caseId, Guid itemId, string location, CancellationToken ct = default);
    Task DeleteQuarantinedAsync(string tenantId, string caseId, Guid itemId, string location, CancellationToken ct = default);
}

public interface IMagAuditMalwareScanner
{
    Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default);
}

public interface IMagAuditTextExtractor
{
    Task<string> ExtractAsync(Stream content, string fileName, CancellationToken ct = default);
}

public interface IMagAuditDocumentClassifier
{
    Task<DocumentClassification> ClassifyAsync(string fileName, string text, DocumentImportItem item, CancellationToken ct = default);
}

public interface IMagAuditBulkDocumentIngestion
{
    Task<DocumentImportBatch> CreateBatchAsync(string tenantId, string caseId, string name, string createdBy, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentImportItem>> RegisterInventoryAsync(string tenantId, Guid batchId, IEnumerable<DocumentImportCandidate> candidates, CancellationToken ct = default);
    Task<DocumentImportItem> ProcessContentAsync(string tenantId, Guid batchId, Guid itemId, Stream content, CancellationToken ct = default);
}

public sealed class MagAuditBulkDocumentIngestion : IMagAuditBulkDocumentIngestion
{
    public const int MaxFilesPerBatch = 500;
    public const long MaxFileBytes = 250L * 1024 * 1024;
    public const double AutoAcceptConfidence = 0.92;

    private static readonly Regex Sha256Pattern = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".tif", ".tiff", ".png", ".jpg", ".jpeg", ".txt", ".csv", ".xlsx", ".docx", ".xml"
    };

    private readonly IMagAuditDocumentImportStore _imports;
    private readonly IMagAuditEvidenceStore _evidence;
    private readonly IMagAuditMalwareScanner _scanner;
    private readonly IMagAuditTextExtractor _extractor;
    private readonly IMagAuditDocumentClassifier _classifier;
    private readonly IAuditLog _audit;

    public MagAuditBulkDocumentIngestion(
        IMagAuditDocumentImportStore imports,
        IMagAuditEvidenceStore evidence,
        IMagAuditMalwareScanner scanner,
        IMagAuditTextExtractor extractor,
        IMagAuditDocumentClassifier classifier,
        IAuditLog audit)
    {
        _imports = imports;
        _evidence = evidence;
        _scanner = scanner;
        _extractor = extractor;
        _classifier = classifier;
        _audit = audit;
    }

    public async Task<DocumentImportBatch> CreateBatchAsync(
        string tenantId, string caseId, string name, string createdBy, CancellationToken ct = default)
    {
        RequireScope(tenantId, caseId);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Batch name is required.");
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("Responsible identity is required.");

        var batch = new DocumentImportBatch
        {
            TenantId = tenantId.Trim(),
            CaseId = caseId.Trim(),
            Name = name.Trim(),
            CreatedBy = createdBy.Trim()
        };
        await _imports.AddBatchAsync(batch, ct);
        await AuditAsync(batch, "MagAuditDocumentImportBatchCreated", createdBy,
            $"Document import batch '{batch.Name}' created for case '{batch.CaseId}'.", ct);
        return batch;
    }

    public async Task<IReadOnlyList<DocumentImportItem>> RegisterInventoryAsync(
        string tenantId, Guid batchId, IEnumerable<DocumentImportCandidate> candidates, CancellationToken ct = default)
    {
        var batch = await RequiredBatchAsync(tenantId, batchId, ct);
        var incoming = candidates.ToList();
        if (incoming.Count == 0) throw new ArgumentException("At least one file is required.");
        if (batch.Items.Count + incoming.Count > MaxFilesPerBatch)
            throw new ArgumentException($"A batch may contain at most {MaxFilesPerBatch} files.");

        var added = new List<DocumentImportItem>();
        foreach (var candidate in incoming)
        {
            var normalized = NormalizeRelativePath(candidate.RelativePath);
            var extension = Path.GetExtension(normalized);
            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException($"Unsupported file type '{extension}' for '{normalized}'.");
            if (candidate.SizeBytes <= 0 || candidate.SizeBytes > MaxFileBytes)
                throw new ArgumentException($"File '{normalized}' must be between 1 byte and {MaxFileBytes} bytes.");
            if (!Sha256Pattern.IsMatch(candidate.Sha256))
                throw new ArgumentException($"File '{normalized}' has an invalid SHA-256 value.");

            var item = new DocumentImportItem
            {
                BatchId = batch.BatchId,
                TenantId = batch.TenantId,
                CaseId = batch.CaseId,
                RelativePath = normalized,
                FileName = Path.GetFileName(normalized),
                SizeBytes = candidate.SizeBytes,
                Sha256 = candidate.Sha256.ToLowerInvariant(),
                DeclaredClientId = candidate.DeclaredClientId,
                DeclaredTaxYear = candidate.DeclaredTaxYear,
                DeclaredDocumentType = candidate.DeclaredDocumentType
            };
            var duplicate = await _imports.FindAcceptedByHashAsync(batch.TenantId, batch.CaseId, item.Sha256, ct);
            if (duplicate is not null)
            {
                item.State = DocumentImportState.DuplicateReview;
                item.DuplicateOfItemId = duplicate.ItemId;
            }
            batch.Items.Add(item);
            await _imports.SaveItemAsync(item, ct);
            added.Add(item);
        }
        return added;
    }

    public async Task<DocumentImportItem> ProcessContentAsync(
        string tenantId, Guid batchId, Guid itemId, Stream content, CancellationToken ct = default)
    {
        var batch = await RequiredBatchAsync(tenantId, batchId, ct);
        var item = batch.Items.SingleOrDefault(i => i.ItemId == itemId)
            ?? throw new KeyNotFoundException("Import item was not found in this tenant and batch.");
        if (item.State is not (DocumentImportState.Inventoried or DocumentImportState.Uploading))
            throw new InvalidOperationException($"Item in state {item.State} cannot accept content.");

        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        if (buffered.Length != item.SizeBytes)
            return await RejectAsync(batch, item, "Uploaded size does not match the inventory.", ct);
        var actualHash = Convert.ToHexString(SHA256.HashData(buffered.ToArray())).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(item.Sha256)))
            return await RejectAsync(batch, item, "Uploaded SHA-256 does not match the inventory.", ct);

        buffered.Position = 0;
        item.EvidenceLocation = await _evidence.PutQuarantinedAsync(
            batch.TenantId, batch.CaseId, item.ItemId, item.FileName, buffered, ct);
        item.State = DocumentImportState.Quarantined;
        await _imports.SaveItemAsync(item, ct);

        buffered.Position = 0;
        item.MalwareScan = await _scanner.ScanAsync(buffered, item.FileName, ct);
        if (!item.MalwareScan.IsClean)
        {
            await _evidence.DeleteQuarantinedAsync(batch.TenantId, batch.CaseId, item.ItemId, item.EvidenceLocation, ct);
            return await RejectAsync(batch, item, $"Security scan rejected the file: {item.MalwareScan.Detail}", ct);
        }

        buffered.Position = 0;
        item.ExtractedText = await _extractor.ExtractAsync(buffered, item.FileName, ct);
        item.Classification = await _classifier.ClassifyAsync(item.FileName, item.ExtractedText, item, ct);
        item.State = item.Classification.Confidence >= AutoAcceptConfidence
            ? DocumentImportState.Accepted
            : DocumentImportState.ReviewRequired;
        if (item.State == DocumentImportState.Accepted)
            await _evidence.MarkAcceptedAsync(batch.TenantId, batch.CaseId, item.ItemId, item.EvidenceLocation, ct);
        await _imports.SaveItemAsync(item, ct);
        await AuditAsync(batch, "MagAuditDocumentProcessed", batch.CreatedBy,
            $"Document '{item.FileName}' processed with state {item.State}.", ct,
            new() { ["itemId"] = item.ItemId.ToString(), ["sha256"] = item.Sha256,
                ["documentType"] = item.Classification.DocumentType,
                ["confidence"] = item.Classification.Confidence.ToString("0.000") });
        return item;
    }

    private async Task<DocumentImportItem> RejectAsync(
        DocumentImportBatch batch, DocumentImportItem item, string error, CancellationToken ct)
    {
        item.State = DocumentImportState.Rejected;
        item.Errors.Add(error);
        await _imports.SaveItemAsync(item, ct);
        await AuditAsync(batch, "MagAuditDocumentRejected", batch.CreatedBy,
            $"Document '{item.FileName}' rejected: {error}", ct,
            new() { ["itemId"] = item.ItemId.ToString() });
        return item;
    }

    private async Task<DocumentImportBatch> RequiredBatchAsync(string tenantId, Guid batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant is required.");
        return await _imports.GetBatchAsync(tenantId.Trim(), batchId, ct)
            ?? throw new KeyNotFoundException("Import batch was not found in this tenant.");
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Relative path is required.");
        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(p => p is ".." or "." or ""))
            throw new ArgumentException("File path must be a safe relative path without traversal segments.");
        return normalized;
    }

    private static void RequireScope(string tenantId, string caseId)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant is required.");
        if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("Case is required.");
    }

    private Task AuditAsync(DocumentImportBatch batch, string category, string actor, string summary,
        CancellationToken ct, Dictionary<string, string>? extra = null)
    {
        var detail = new Dictionary<string, string>
        {
            ["batchId"] = batch.BatchId.ToString(), ["caseId"] = batch.CaseId
        };
        if (extra is not null) foreach (var pair in extra) detail[pair.Key] = pair.Value;
        return _audit.AppendAsync(new AuditEntry
        {
            TenantId = batch.TenantId,
            CorrelationId = batch.BatchId,
            Category = category,
            Actor = actor,
            Summary = summary,
            Detail = detail
        }, ct);
    }
}
