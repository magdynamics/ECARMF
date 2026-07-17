using System.Text;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Application.Ingestion;

public sealed record BulkImportResult(
    int TotalRows,
    int Imported,
    int Failed,
    IReadOnlyList<string> Errors,
    Guid DocumentId);

public interface IBulkImportService
{
    /// <summary>Imports a CSV of historical records: the header row names the
    /// payload fields, every data row becomes a typed record submitted
    /// through the standard intake — same rules, scoring, benchmarks, and
    /// audit as live data. The CSV itself is archived as evidence with the
    /// record lineage. Row failures are collected, never fatal.</summary>
    /// <param name="unitRef">Unit every row is attributed to; a per-row
    /// "unitRef" (or "unit") column overrides it, so one spreadsheet can
    /// carry many locations. Null = tenant-wide.</param>
    Task<BulkImportResult> ImportCsvAsync(
        string tenantId, string recordType, string fileName, byte[] csvContent,
        string submittedBy, string? unitRef = null, CancellationToken ct = default);
}

/// <summary>
/// Day-one history: new clients arrive with years of records in
/// spreadsheets. This service walks a CSV through the exact same intake
/// path as live submissions — no side door into the database — so imported
/// history carries full provenance and triggers the same intelligence.
/// </summary>
public class BulkImportService : IBulkImportService
{
    /// <summary>Rows beyond this are rejected up front — split the file
    /// rather than let one request monopolize the pipeline.</summary>
    public const int MaxRows = 10_000;

    private const int MaxReportedErrors = 50;

    private readonly ITransactionIntakeService _intake;
    private readonly IDocumentLibrary _library;
    private readonly IAuditLog _audit;
    private readonly IOrgUnitStore? _units;

    public BulkImportService(
        ITransactionIntakeService intake, IDocumentLibrary library, IAuditLog audit,
        IOrgUnitStore? units = null)
    {
        _intake = intake;
        _library = library;
        _audit = audit;
        _units = units;
    }

    public async Task<BulkImportResult> ImportCsvAsync(
        string tenantId, string recordType, string fileName, byte[] csvContent,
        string submittedBy, string? unitRef = null, CancellationToken ct = default)
    {
        if (_units is not null)
        {
            var unitError = await UnitScope.ValidateAsync(_units, tenantId, unitRef, ct);
            if (unitError is not null)
            {
                throw new ArgumentException(unitError);
            }
        }

        var rows = ParseCsv(Encoding.UTF8.GetString(csvContent).TrimStart('﻿'));
        if (rows.Count < 2)
        {
            throw new ArgumentException("The CSV needs a header row and at least one data row.");
        }

        var header = rows[0].Select(h => h.Trim()).ToList();
        if (header.All(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The CSV header row is empty.");
        }

        var dataRows = rows.Count - 1;
        if (dataRows > MaxRows)
        {
            throw new ArgumentException(
                $"The CSV has {dataRows} data rows; the limit per import is {MaxRows}. Split the file.");
        }

        var recordIds = new List<Guid>();
        var errors = new List<string>();
        var validatedUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cells = rows[i];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue; // trailing blank lines are not data
            }

            var payload = new Dictionary<string, string>();
            for (var c = 0; c < header.Count && c < cells.Count; c++)
            {
                if (!string.IsNullOrWhiteSpace(header[c]))
                {
                    payload[header[c]] = cells[c].Trim();
                }
            }

            // Unit attribution: a per-row unitRef/unit column overrides the
            // import-level unit (validated per distinct value, cached), so a
            // single spreadsheet can carry many locations without ambiguity.
            var rowUnit = payload.TryGetValue("unitRef", out var u1) && !string.IsNullOrWhiteSpace(u1) ? u1.Trim()
                : payload.TryGetValue("unit", out var u2) && !string.IsNullOrWhiteSpace(u2) ? u2.Trim()
                : unitRef;

            try
            {
                if (rowUnit is not null && rowUnit != unitRef && _units is not null && validatedUnits.Add(rowUnit))
                {
                    var rowUnitError = await UnitScope.ValidateAsync(_units, tenantId, rowUnit, ct);
                    if (rowUnitError is not null)
                    {
                        validatedUnits.Remove(rowUnit);
                        throw new ArgumentException(rowUnitError);
                    }
                }

                var receipt = await _intake.ReceiveAsync(
                    new TransactionSubmission(tenantId, recordType, submittedBy, payload, UnitRef: rowUnit), ct);
                recordIds.Add(receipt.TransactionId);
            }
            catch (Exception ex)
            {
                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add($"Row {i + 1}: {ex.Message}");
                }
            }
        }

        var failed = dataRows - recordIds.Count
            - rows.Skip(1).Count(r => r.All(string.IsNullOrWhiteSpace));

        // The spreadsheet is evidence like any other upload: archived
        // verbatim with the lineage to every record it produced.
        var document = await _library.ArchiveAsync(new SourceDocument
        {
            TenantId = tenantId,
            FileName = fileName,
            MediaType = "csv",
            SourceId = "bulk-import",
            SourceCategory = "bulk-import",
            UploadedBy = submittedBy,
            UnitRef = string.IsNullOrWhiteSpace(unitRef) ? null : unitRef.Trim(),
            RecordIds = recordIds,
            Metadata = new Dictionary<string, string>
            {
                ["contentType"] = "text/csv",
                ["recordType"] = recordType,
                ["rows"] = dataRows.ToString(),
                ["imported"] = recordIds.Count.ToString(),
                ["failed"] = failed.ToString()
            }
        }, csvContent, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.BulkImportCompleted,
            Actor = submittedBy,
            Summary = $"Bulk import '{fileName}' ({recordType}): {recordIds.Count} of {dataRows} row(s) imported"
                + (failed > 0 ? $", {failed} failed." : "."),
            Detail = new Dictionary<string, string>
            {
                ["documentId"] = document.Id.ToString(),
                ["recordType"] = recordType,
                ["rows"] = dataRows.ToString(),
                ["imported"] = recordIds.Count.ToString(),
                ["failed"] = failed.ToString()
            }
        }, ct);

        return new BulkImportResult(dataRows, recordIds.Count, failed, errors, document.Id);
    }

    /// <summary>RFC 4180 CSV: quoted fields may contain commas, newlines,
    /// and doubled quotes. No dependency, no surprises.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    cell.Append(ch);
                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
