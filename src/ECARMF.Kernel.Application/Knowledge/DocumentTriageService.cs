using System.Globalization;
using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Knowledge;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Application.Knowledge;

/// <summary>Persistence for AI document-allocation recommendations.</summary>
public interface IDocumentAllocationStore
{
    Task AddAsync(DocumentAllocation allocation, CancellationToken ct = default);
    Task<DocumentAllocation?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentAllocation>> GetByStatusAsync(string tenantId, string? status, CancellationToken ct = default);
    Task UpdateAsync(DocumentAllocation allocation, CancellationToken ct = default);
}

public sealed record TriageOutcome(bool Success, DocumentAllocation? Allocation, string? Error);

public interface IDocumentTriageService
{
    /// <summary>Reads one document, recommends which of the tenant's units it
    /// belongs to (via the tenant's AI backend), archives it to the library as
    /// pending, and records the recommendation for human review.</summary>
    Task<TriageOutcome> AnalyzeAsync(
        string tenantId, string fileName, string documentText, byte[]? originalContent,
        string actor, CancellationToken ct = default);

    /// <summary>A human's decision: file the document under the chosen unit
    /// (the AI's recommendation, or a correction). Stamps the library
    /// document's UnitRef so downstream scoping is correct.</summary>
    Task<TriageOutcome> DecideAsync(
        string tenantId, Guid allocationId, string? unitRef, User decidedBy, CancellationToken ct = default);
}

/// <summary>
/// The document-triage engine. Given the tenant's organizational units and a
/// document's text, it asks the model "whose is this, and what is it?" — never
/// filing anything itself; it produces a recommendation a human confirms. This
/// turns a 1,000-document mixed pile for a multi-entity group into a reviewable
/// queue where each document is pre-sorted, explained, and one click from filed.
/// </summary>
public class DocumentTriageService : IDocumentTriageService
{
    private const string SystemPrompt =
        "You are a document-routing and extraction analyst inside the ECARMF platform. You are given " +
        "(a) the business group's organizational units, (b) the catalog of known document types, and " +
        "(c) the text of ONE document. Do three things: route it to a unit, classify its type, and " +
        "extract its key data. Rules: choose the unit only from the provided unit slugs (null if it " +
        "applies to the whole group or you're unsure — never invent a unit); classify documentType to " +
        "one of the catalog type keys (or \"other\"); extract subjectKey (the account number, employee " +
        "name, or entity the document is primarily about), period (the year or period it covers), and " +
        "fields (a flat object of the type's numeric key fields as plain numbers — no currency symbols " +
        "or commas). Never invent numbers; omit a field you cannot find. Respond with ONLY JSON: " +
        "{\"unitRef\": string|null, \"documentType\": string, \"confidence\": number 0..1, " +
        "\"reasoning\": string, \"subjectKey\": string|null, \"period\": string|null, " +
        "\"fields\": object}.";

    private readonly IOrgUnitStore _units;
    private readonly ILanguageModelProvider _llmProvider;
    private readonly IDocumentLibrary _library;
    private readonly IDocumentAllocationStore _allocations;
    private readonly IExtractedDataStore _extracted;
    private readonly IAuditLog _audit;

    public DocumentTriageService(
        IOrgUnitStore units,
        ILanguageModelProvider llmProvider,
        IDocumentLibrary library,
        IDocumentAllocationStore allocations,
        IExtractedDataStore extracted,
        IAuditLog audit)
    {
        _units = units;
        _llmProvider = llmProvider;
        _library = library;
        _allocations = allocations;
        _extracted = extracted;
        _audit = audit;
    }

    public async Task<TriageOutcome> AnalyzeAsync(
        string tenantId, string fileName, string documentText, byte[]? originalContent,
        string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
            return new TriageOutcome(false, null, "The document contains no readable text.");

        var units = (await _units.GetAllAsync(tenantId, ct))
            .Where(u => string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (units.Count == 0)
            return new TriageOutcome(false, null, "This tenant has no organizational units to allocate documents to. Add units under Organization first.");

        var llm = await _llmProvider.GetForTenantAsync(tenantId, ct);
        if (!llm.IsConfigured)
            return new TriageOutcome(false, null,
                "Document triage needs this tenant's AI backend (Setup → AI Backend — a local Ollama/LM Studio server needs no key).");

        // Archive first: the evidence exists whether or not the AI succeeds.
        var doc = await _library.ArchiveAsync(new SourceDocument
        {
            TenantId = tenantId,
            FileName = fileName,
            MediaType = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "text",
            SourceId = "document-triage",
            SourceCategory = "triage-pending",
            UploadedBy = actor
        }, originalContent ?? System.Text.Encoding.UTF8.GetBytes(documentText), ct);

        var unitList = string.Join("\n", units.Select(u =>
            $"- {u.UnitId} :: {u.Name} ({u.UnitType}{(string.IsNullOrWhiteSpace(u.Industry) ? "" : ", " + u.Industry)})"
            + (string.IsNullOrWhiteSpace(u.Notes) ? "" : $" — {u.Notes}")));

        var allocation = new DocumentAllocation
        {
            TenantId = tenantId,
            DocumentId = doc.Id,
            FileName = fileName,
            CreatedBy = actor
        };
        Recommendation? parsed = null;

        try
        {
            var truncated = documentText.Length > 6000 ? documentText[..6000] : documentText;
            var response = await llm.CompleteAsync(SystemPrompt,
                $"Organizational units (slug :: name (type) — notes):\n{unitList}\n\n" +
                $"Known document types:\n{DocumentTypeCatalog.ForPrompt()}\n\n" +
                $"Document: {fileName}\n\nText:\n{truncated}", ct);

            parsed = ParseRecommendation(response);
            if (parsed is not null)
            {
                var match = string.IsNullOrWhiteSpace(parsed.UnitRef)
                    ? null
                    : units.FirstOrDefault(u => string.Equals(u.UnitId, parsed.UnitRef, StringComparison.OrdinalIgnoreCase));
                allocation.RecommendedUnitRef = match?.UnitId; // dropped if hallucinated
                allocation.RecommendedUnitName = match?.Name;
                allocation.DocumentType = parsed.DocumentType;
                allocation.Confidence = Math.Clamp(parsed.Confidence, 0m, 1m);
                allocation.Reasoning = parsed.Reasoning;
            }
            else
            {
                allocation.Reasoning = "The model did not return a usable recommendation — review and file manually.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            allocation.Reasoning = $"AI analysis failed ({ex.Message}) — review and file manually.";
        }

        await _allocations.AddAsync(allocation, ct);

        // Capture the structured data so reconciliation can query real numbers.
        // Only for a recognized canonical type with extracted fields.
        if (parsed is not null && DocumentTypeCatalog.Find(parsed.DocumentType) is not null && parsed.Fields.Count > 0)
        {
            await _extracted.AddAsync(new ExtractedDocumentData
            {
                TenantId = tenantId,
                DocumentId = doc.Id,
                FileName = fileName,
                DocumentType = parsed.DocumentType,
                UnitRef = allocation.RecommendedUnitRef,
                SubjectKey = parsed.SubjectKey?.ToLowerInvariant(),
                Period = parsed.Period,
                Fields = new Dictionary<string, string>(parsed.Fields, StringComparer.OrdinalIgnoreCase),
                Backend = llm.ModelReference
            }, ct);
        }

        return new TriageOutcome(true, allocation, null);
    }

    public async Task<TriageOutcome> DecideAsync(
        string tenantId, Guid allocationId, string? unitRef, User decidedBy, CancellationToken ct = default)
    {
        var allocation = await _allocations.GetAsync(tenantId, allocationId, ct);
        if (allocation is null)
            return new TriageOutcome(false, null, "Allocation not found.");
        if (allocation.Status != DocumentAllocationStatuses.Pending)
            return new TriageOutcome(false, null, "This document has already been filed.");

        // Validate the chosen unit (null = tenant-wide/group).
        var unitError = await UnitScope.ValidateAsync(_units, tenantId, unitRef, ct);
        if (unitError is not null)
            return new TriageOutcome(false, null, unitError);

        var chosen = string.IsNullOrWhiteSpace(unitRef) ? null : unitRef.Trim();
        allocation.DecidedUnitRef = chosen;
        allocation.DecidedBy = decidedBy.Identifier;
        allocation.DecidedAt = DateTimeOffset.UtcNow;
        allocation.Status = string.Equals(chosen, allocation.RecommendedUnitRef, StringComparison.OrdinalIgnoreCase)
            ? DocumentAllocationStatuses.Confirmed
            : DocumentAllocationStatuses.Reassigned;
        await _allocations.UpdateAsync(allocation, ct);

        // Stamp the library document with the decided unit and move it out of
        // the pending category — filed to its entity.
        await _library.SetUnitAndCategoryAsync(tenantId, allocation.DocumentId, chosen, "triage-filed", ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = allocation.Id,
            Category = AuditCategories.RecordReceived,
            Actor = decidedBy.Identifier,
            Summary = $"Document '{allocation.FileName}' filed to {(chosen ?? "the whole group")} " +
                      $"({allocation.Status.ToLowerInvariant()}; AI recommended {allocation.RecommendedUnitRef ?? "none"}).",
            Detail = new Dictionary<string, string>
            {
                ["documentId"] = allocation.DocumentId.ToString(),
                ["recommendedUnit"] = allocation.RecommendedUnitRef ?? "(none)",
                ["decidedUnit"] = chosen ?? "(tenant-wide)",
                ["confidence"] = allocation.Confidence.ToString(CultureInfo.InvariantCulture),
                ["status"] = allocation.Status
            }
        }, ct);

        return new TriageOutcome(true, allocation, null);
    }

    private sealed record Recommendation(
        string? UnitRef, string DocumentType, decimal Confidence, string Reasoning,
        string? SubjectKey, string? Period, Dictionary<string, string> Fields);

    private static Recommendation? ParseRecommendation(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            string? unit = root.TryGetProperty("unitRef", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            var type = root.TryGetProperty("documentType", out var t) ? t.GetString() ?? "other" : "other";
            var conf = root.TryGetProperty("confidence", out var c) && c.TryGetDecimal(out var cd) ? cd : 0m;
            var reason = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            string? subject = root.TryGetProperty("subjectKey", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            string? period = root.TryGetProperty("period", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                foreach (var prop in f.EnumerateObject())
                {
                    var val = prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetRawText()
                        : prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(val)) fields[prop.Name] = val;
                }
            return new Recommendation(unit, type, conf, reason, subject, period, fields);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
