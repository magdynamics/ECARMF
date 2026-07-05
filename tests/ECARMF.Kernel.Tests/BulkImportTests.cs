using System.Text;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class RecordingIntakeService : ITransactionIntakeService
{
    public List<TransactionSubmission> Received { get; } = [];
    public Func<TransactionSubmission, bool>? FailWhen { get; set; }

    public Task<TransactionReceipt> ReceiveAsync(TransactionSubmission submission, CancellationToken ct = default)
    {
        if (FailWhen?.Invoke(submission) == true)
        {
            throw new InvalidOperationException("simulated intake failure");
        }

        Received.Add(submission);
        return Task.FromResult(new TransactionReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow, true, null));
    }
}

/// <summary>Years of history in a spreadsheet enter through the same front
/// door as live records: header row names the fields, each row is a full
/// intake, the CSV archives as evidence, row failures never sink the batch.</summary>
public class BulkImportTests
{
    private const string Tenant = "tenant-a";

    private readonly RecordingIntakeService _intake = new();
    private readonly InMemoryDocumentLibrary _library = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly BulkImportService _service;

    public BulkImportTests()
    {
        _service = new BulkImportService(_intake, _library, _audit);
    }

    private Task<BulkImportResult> Import(string csv, string recordType = "JournalEntry") =>
        _service.ImportCsvAsync(Tenant, recordType, "history.csv", Encoding.UTF8.GetBytes(csv), "owner@a");

    [Fact]
    public async Task Rows_become_records_through_the_standard_intake()
    {
        var result = await Import(
            "valueDate,amount,description\n" +
            "2023-01-15,1200.50,January rent\n" +
            "2023-02-15,1200.50,February rent\n");

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, _intake.Received.Count);
        var first = _intake.Received[0];
        Assert.Equal(Tenant, first.TenantId);
        Assert.Equal("JournalEntry", first.TransactionType);
        Assert.Equal("2023-01-15", first.Payload["valueDate"]);
        Assert.Equal("January rent", first.Payload["description"]);
    }

    [Fact]
    public async Task Quoted_cells_with_commas_and_newlines_parse_correctly()
    {
        var result = await Import(
            "name,notes\n" +
            "\"Smith, John\",\"line one\nline two with \"\"quotes\"\"\"\n");

        Assert.Equal(1, result.Imported);
        Assert.Equal("Smith, John", _intake.Received[0].Payload["name"]);
        Assert.Equal("line one\nline two with \"quotes\"", _intake.Received[0].Payload["notes"]);
    }

    [Fact]
    public async Task A_failing_row_is_reported_without_sinking_the_batch()
    {
        _intake.FailWhen = s => s.Payload["amount"] == "bad";

        var result = await Import(
            "valueDate,amount\n2023-01-01,100\n2023-01-02,bad\n2023-01-03,300\n");

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Failed);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Row 3:", error);
    }

    [Fact]
    public async Task The_spreadsheet_is_archived_with_record_lineage_and_audited()
    {
        var result = await Import("f\n1\n2\n");

        var (document, _) = Assert.Single(_library.Items);
        Assert.Equal("bulk-import", document.SourceCategory);
        Assert.Equal(2, document.RecordIds.Count);
        Assert.Equal(result.DocumentId, document.Id);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.BulkImportCompleted);
    }

    [Fact]
    public async Task Header_only_or_oversized_files_are_rejected_up_front()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Import("valueDate,amount\n"));

        var big = new StringBuilder("f\n");
        for (var i = 0; i <= BulkImportService.MaxRows; i++) big.Append("1\n");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Import(big.ToString()));
        Assert.Contains("Split the file", ex.Message);
        Assert.Empty(_intake.Received); // rejected before any intake
    }
}
