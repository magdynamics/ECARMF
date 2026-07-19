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
        "You are a document-routing analyst inside the ECARMF platform. You are given (a) the list " +
        "of a business group's organizational units — legal entities, locations, divisions, properties, " +
        "and principals — and (b) the text of ONE document. Decide which SINGLE unit the document most " +
        "likely belongs to, what kind of document it is, and how confident you are. Rules: choose only " +
        "from the provided unit slugs; if the document plainly applies to the whole group or you cannot " +
        "confidently place it, return unitRef null; never invent a unit; be honest with confidence. " +
        "Respond with ONLY a JSON object: {\"unitRef\": string|null, \"documentType\": string, " +
        "\"confidence\": number 0..1, \"reasoning\": string (one sentence)}.";

    private readonly IOrgUnitStore _units;
    private readonly ILanguageModelProvider _llmProvider;
    private readonly IDocumentLibrary _library;
    private readonly IDocumentAllocationStore _allocations;
    private readonly IAuditLog _audit;

    public DocumentTriageService(
        IOrgUnitStore units,
        ILanguageModelProvider llmProvider,
        IDocumentLibrary library,
        IDocumentAllocationStore allocations,
        IAuditLog audit)
    {
        _units = units;
        _llmProvider = llmProvider;
        _library = library;
        _allocations = allocations;
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

        try
        {
            var truncated = documentText.Length > 6000 ? documentText[..6000] : documentText;
            var response = await llm.CompleteAsync(SystemPrompt,
                $"Organizational units (slug :: name (type) — notes):\n{unitList}\n\nDocument: {fileName}\n\nText:\n{truncated}", ct);

            var parsed = ParseRecommendation(response);
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

    private sealed record Recommendation(string? UnitRef, string DocumentType, decimal Confidence, string Reasoning);

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
            var type = root.TryGetProperty("documentType", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
            var conf = root.TryGetProperty("confidence", out var c) && c.TryGetDecimal(out var cd) ? cd : 0m;
            var reason = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            return new Recommendation(unit, type, conf, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
