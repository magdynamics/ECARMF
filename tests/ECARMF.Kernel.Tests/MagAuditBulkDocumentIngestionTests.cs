using System.Security.Cryptography;
using System.Text;
using ECARMF.Kernel.Application.MagAudit.Documents;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class MagAuditBulkDocumentIngestionTests
{
    private readonly ImportStore _imports = new();
    private readonly EvidenceStore _evidence = new();
    private readonly InMemoryAuditLog _audit = new();

    private MagAuditBulkDocumentIngestion Service(
        bool clean = true, double confidence = 0.96) => new(
        _imports, _evidence,
        new Scanner(clean), new Extractor(), new Classifier(confidence), _audit);

    [Fact]
    public async Task Clean_matching_document_is_accepted_with_provenance_and_audit()
    {
        var service = Service();
        var content = Encoding.UTF8.GetBytes("Form W-2 2024 wages 100000 withholding 12000");
        var batch = await service.CreateBatchAsync("tenant-30-mag-audit", "case-2024-1120s", "Initial records", "staff@mag");
        var item = Assert.Single(await service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("Client-1/2024/W2.pdf", content)]));

        var result = await service.ProcessContentAsync(batch.TenantId, batch.BatchId, item.ItemId, new MemoryStream(content));

        Assert.Equal(DocumentImportState.Accepted, result.State);
        Assert.Equal("W-2", result.Classification?.DocumentType);
        Assert.Equal("2024", result.Classification?.TaxYear);
        Assert.NotNull(result.MalwareScan);
        Assert.True(_evidence.Accepted.Contains(result.ItemId));
        Assert.Contains(_audit.Items, a => a.Category == "MagAuditDocumentProcessed" && a.TenantId == batch.TenantId);
    }

    [Fact]
    public async Task Hash_mismatch_is_rejected_before_security_and_extraction()
    {
        var service = Service();
        var expected = Encoding.UTF8.GetBytes("expected");
        var batch = await service.CreateBatchAsync("tenant-30-mag-audit", "case-a", "Batch", "staff@mag");
        var item = Assert.Single(await service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("return.pdf", expected)]));

        var result = await service.ProcessContentAsync(batch.TenantId, batch.BatchId, item.ItemId,
            new MemoryStream(Encoding.UTF8.GetBytes("tampered")));

        Assert.Equal(DocumentImportState.Rejected, result.State);
        Assert.Contains(result.Errors, e => e.Contains("size") || e.Contains("SHA-256"));
        Assert.Empty(_evidence.Accepted);
    }

    [Fact]
    public async Task Malware_result_deletes_quarantine_and_rejects_document()
    {
        var service = Service(clean: false);
        var content = Encoding.UTF8.GetBytes("suspicious content");
        var batch = await service.CreateBatchAsync("tenant-30-mag-audit", "case-a", "Batch", "staff@mag");
        var item = Assert.Single(await service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("statement.pdf", content)]));

        var result = await service.ProcessContentAsync(batch.TenantId, batch.BatchId, item.ItemId, new MemoryStream(content));

        Assert.Equal(DocumentImportState.Rejected, result.State);
        Assert.Contains(item.ItemId, _evidence.Deleted);
        Assert.Empty(_evidence.Accepted);
    }

    [Fact]
    public async Task Low_confidence_requires_human_review_and_remains_quarantined()
    {
        var service = Service(confidence: 0.70);
        var content = Encoding.UTF8.GetBytes("unclear scan");
        var batch = await service.CreateBatchAsync("tenant-30-mag-audit", "case-a", "Batch", "staff@mag");
        var item = Assert.Single(await service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("unknown.pdf", content)]));

        var result = await service.ProcessContentAsync(batch.TenantId, batch.BatchId, item.ItemId, new MemoryStream(content));

        Assert.Equal(DocumentImportState.ReviewRequired, result.State);
        Assert.Empty(_evidence.Accepted);
    }

    [Fact]
    public async Task Unsafe_paths_unsupported_types_and_cross_tenant_batches_fail_closed()
    {
        var service = Service();
        var content = Encoding.UTF8.GetBytes("data");
        var batch = await service.CreateBatchAsync("tenant-30-mag-audit", "case-a", "Batch", "staff@mag");

        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("../outside.pdf", content)]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterInventoryAsync(batch.TenantId, batch.BatchId,
            [Candidate("payload.exe", content)]));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RegisterInventoryAsync("other-tenant", batch.BatchId,
            [Candidate("safe.pdf", content)]));
    }

    private static DocumentImportCandidate Candidate(string path, byte[] bytes) => new(
        path, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private sealed class ImportStore : IMagAuditDocumentImportStore
    {
        private readonly List<DocumentImportBatch> _batches = [];
        public Task AddBatchAsync(DocumentImportBatch batch, CancellationToken ct = default)
        { _batches.Add(batch); return Task.CompletedTask; }
        public Task<DocumentImportBatch?> GetBatchAsync(string tenantId, Guid batchId, CancellationToken ct = default) =>
            Task.FromResult(_batches.SingleOrDefault(b => b.TenantId == tenantId && b.BatchId == batchId));
        public Task SaveItemAsync(DocumentImportItem item, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentImportItem?> FindAcceptedByHashAsync(string tenantId, string caseId, string sha256, CancellationToken ct = default) =>
            Task.FromResult(_batches.Where(b => b.TenantId == tenantId && b.CaseId == caseId)
                .SelectMany(b => b.Items).FirstOrDefault(i => i.Sha256 == sha256 && i.State == DocumentImportState.Accepted));
    }

    private sealed class EvidenceStore : IMagAuditEvidenceStore
    {
        public HashSet<Guid> Accepted { get; } = [];
        public HashSet<Guid> Deleted { get; } = [];
        public Task<string> PutQuarantinedAsync(string tenantId, string caseId, Guid itemId, string fileName, Stream content, CancellationToken ct = default) =>
            Task.FromResult($"quarantine/{tenantId}/{caseId}/{itemId}/{fileName}");
        public Task MarkAcceptedAsync(string tenantId, string caseId, Guid itemId, string location, CancellationToken ct = default)
        { Accepted.Add(itemId); return Task.CompletedTask; }
        public Task DeleteQuarantinedAsync(string tenantId, string caseId, Guid itemId, string location, CancellationToken ct = default)
        { Deleted.Add(itemId); return Task.CompletedTask; }
    }

    private sealed class Scanner(bool clean) : IMagAuditMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default) =>
            Task.FromResult(new MalwareScanResult(clean, "test-scanner", "1", clean ? "clean" : "malware"));
    }

    private sealed class Extractor : IMagAuditTextExtractor
    {
        public async Task<string> ExtractAsync(Stream content, string fileName, CancellationToken ct = default)
        { using var reader = new StreamReader(content, leaveOpen: true); return await reader.ReadToEndAsync(ct); }
    }

    private sealed class Classifier(double confidence) : IMagAuditDocumentClassifier
    {
        public Task<DocumentClassification> ClassifyAsync(string fileName, string text, DocumentImportItem item, CancellationToken ct = default) =>
            Task.FromResult(new DocumentClassification(text.Contains("W-2") ? "W-2" : "Unknown",
                text.Contains("2024") ? "2024" : item.DeclaredTaxYear, item.DeclaredClientId, confidence, ["test"]));
    }
}
