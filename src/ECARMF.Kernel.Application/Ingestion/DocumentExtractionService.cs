using System.Text;
using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Library;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Ingestion;

/// <summary>Turns an uploaded file into plain text (PDF text extraction lives
/// in Infrastructure; plain-text formats decode directly).</summary>
public interface IDocumentTextReader
{
    (bool Success, string TextOrError) ReadText(string fileName, byte[] content);
}

public sealed record DocumentExtractionResult(
    bool Success,
    string? RawPayload,
    string Backend,
    IngestionResult? Ingestion,
    IReadOnlyList<string> Errors);

public interface IDocumentExtractor
{
    /// <summary>Extracts structured fields from an unstructured document and
    /// hands them to the connector's normal ingestion path — mapping,
    /// stamping, and intake are the same mechanism every source uses. The
    /// original upload (when provided) is archived verbatim in the library.</summary>
    Task<DocumentExtractionResult> ExtractAndIngestAsync(
        string tenantId, string connectorId, string documentName, string documentText,
        string actorIdentifier, byte[]? originalContent = null, string? unitRef = null,
        CancellationToken ct = default);
}

/// <summary>
/// The document-extraction agent: given a document (invoice, email, statement)
/// and a connector, it produces the raw payload the connector's SchemaTemplate
/// expects — as if the source system had sent it — then runs normal ingestion.
/// Text-format templates extract deterministically via their own regex
/// patterns; json/csv templates use the configured language model. Extraction
/// is audited under the agent's own identity; the record's provenance still
/// comes from the connector, and the human submitter stays SubmittedBy.
/// </summary>
public class DocumentExtractionService : IDocumentExtractor
{
    public const string ActorIdentifier = "system:extractor";

    private readonly IConnectorStore _connectors;
    private readonly ITenantRegistryProvider _registries;
    private readonly IDataSourceConnector _ingestion;
    private readonly ILanguageModelProvider _llmProvider;
    private readonly IAuditLog _audit;
    private readonly IDocumentLibrary? _library;

    public DocumentExtractionService(
        IConnectorStore connectors,
        ITenantRegistryProvider registries,
        IDataSourceConnector ingestion,
        ILanguageModelProvider llmProvider,
        IAuditLog audit,
        IDocumentLibrary? library = null)
    {
        _connectors = connectors;
        _registries = registries;
        _ingestion = ingestion;
        _llmProvider = llmProvider;
        _audit = audit;
        _library = library;
    }

    public async Task<DocumentExtractionResult> ExtractAndIngestAsync(
        string tenantId, string connectorId, string documentName, string documentText,
        string actorIdentifier, byte[]? originalContent = null, string? unitRef = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return new DocumentExtractionResult(false, null, "none", null, ["The document contains no text."]);
        }

        var connector = await _connectors.GetAsync(tenantId, connectorId, ct);
        if (connector is null)
        {
            return new DocumentExtractionResult(false, null, "none", null,
                [$"Connector '{connectorId}' is not configured for this tenant."]);
        }

        if (!_registries.GetFor(tenantId).SchemaTemplates.TryGet(connector.SchemaTemplateId, out var registered))
        {
            return new DocumentExtractionResult(false, null, "none", null,
                [$"Schema template '{connector.SchemaTemplateId}' is not registered by any active package of this tenant."]);
        }

        var template = registered.Declaration;
        string rawPayload;
        string backend;

        if (string.Equals(template.SourceFormat, "text", StringComparison.OrdinalIgnoreCase))
        {
            // The template's own regex patterns are the extractor — no model
            // involved, fully deterministic.
            rawPayload = documentText;
            backend = "regex-template";
        }
        else
        {
            // Tenant-specific credential: extraction runs on this tenant's
            // configured AI backend, never on another tenant's key.
            var llm = await _llmProvider.GetForTenantAsync(tenantId, ct);

            if (!llm.IsConfigured)
            {
                return new DocumentExtractionResult(false, null, "none", null,
                    [$"Template '{template.TemplateId}' is {template.SourceFormat}-format, so document extraction needs the AI backend. " +
                     "Configure this tenant's Anthropic API key (Setup → AI Backend), or use a connector whose template is text-format with regex patterns."]);
            }

            string? extracted;
            try
            {
                var response = await llm.CompleteAsync(
                    ExtractionSystemPrompt, BuildExtractionPrompt(template, documentName, documentText), ct);
                extracted = ExtractPayload(response, template.SourceFormat);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new DocumentExtractionResult(false, null, "llm", null,
                    [$"The extraction model failed: {ex.Message}"]);
            }

            if (extracted is null)
            {
                return new DocumentExtractionResult(false, null, "llm", null,
                    [$"The extraction model did not return a valid {template.SourceFormat} payload."]);
            }

            rawPayload = extracted;
            backend = $"llm:{llm.ModelReference}";
        }

        var ingestion = await _ingestion.IngestAsync(tenantId, connectorId, rawPayload, actorIdentifier, unitRef, ct);

        // Archive the ORIGINAL upload (the PDF/email as received), separate
        // from the extracted payload the ingestion path archives — the
        // library keeps both ends of the lineage.
        if (_library is not null)
        {
            await _library.ArchiveAsync(new SourceDocument
            {
                TenantId = tenantId,
                FileName = documentName,
                MediaType = Path.GetExtension(documentName).TrimStart('.').ToLowerInvariant() is "pdf" ? "pdf" : "text",
                SourceId = connectorId,
                SourceCategory = connector.DomainTag,
                UploadedBy = actorIdentifier,
                ExtractionBackend = backend,
                SchemaTemplateId = template.TemplateId,
                RecordIds = [.. ingestion.RecordIds],
                Metadata = new Dictionary<string, string>
                {
                    ["kind"] = "original-document",
                    ["accepted"] = ingestion.Success.ToString(),
                    ["errors"] = string.Join("; ", ingestion.Errors)
                }
            }, originalContent ?? Encoding.UTF8.GetBytes(documentText), ct);
        }

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid(),
            Category = AuditCategories.DocumentExtracted,
            Actor = ActorIdentifier,
            Summary = $"Document '{documentName}' extracted via {backend} for connector '{connectorId}' — " +
                      (ingestion.Success ? $"{ingestion.RecordIds.Count} record(s) ingested." : "ingestion rejected."),
            Detail = new Dictionary<string, string>
            {
                ["documentName"] = documentName,
                ["connectorId"] = connectorId,
                ["schemaTemplateId"] = template.TemplateId,
                ["backend"] = backend,
                ["submittedBy"] = actorIdentifier,
                ["ingestionSuccess"] = ingestion.Success.ToString(),
                ["recordIds"] = string.Join(",", ingestion.RecordIds),
                ["errors"] = string.Join("; ", ingestion.Errors)
            }
        }, ct);

        return new DocumentExtractionResult(ingestion.Success, rawPayload, backend, ingestion,
            ingestion.Success ? [] : ingestion.Errors);
    }

    private const string ExtractionSystemPrompt =
        "You are the document extraction agent of the ECARMF platform kernel. You convert one " +
        "unstructured business document (invoice, email, bank statement, contract note, report) " +
        "into the exact raw payload the source system would have submitted to a data connector. " +
        "Extract only what the document actually states — never invent, estimate, or infer values " +
        "that are not present. Respond ONLY with the payload itself: no commentary, no markdown fences.";

    private static string BuildExtractionPrompt(
        SchemaTemplateDeclaration template, string documentName, string documentText)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"Target entity type: {template.TargetEntityType}");

        if (string.Equals(template.SourceFormat, "csv", StringComparison.OrdinalIgnoreCase))
        {
            prompt.AppendLine("Output format: CSV with a header row followed by one row per item in the document.");
            prompt.AppendLine("Use exactly these column names:");
        }
        else
        {
            prompt.AppendLine("Output format: a single flat JSON object.");
            prompt.AppendLine("Use exactly these property names (omit a property entirely when the document does not contain it):");
        }

        foreach (var mapping in template.FieldMappings)
        {
            var key = string.IsNullOrWhiteSpace(mapping.RawField) ? mapping.TargetField : mapping.RawField;
            prompt.AppendLine($"- \"{key}\": the document's {mapping.TargetField}{(mapping.Required ? " (required)" : "")}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Rules: dates in ISO 8601; numbers plain, without thousands separators or currency symbols.");
        prompt.AppendLine($"Document '{documentName}':");
        prompt.AppendLine("---");
        prompt.AppendLine(documentText);
        return prompt.ToString();
    }

    /// <summary>Pulls the payload out of a model response, tolerating fences
    /// or prose around it; validates JSON before handing it downstream.</summary>
    internal static string? ExtractPayload(string response, string sourceFormat)
    {
        if (string.Equals(sourceFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            var candidate = response[start..(end + 1)];
            try
            {
                using var _ = JsonDocument.Parse(candidate);
                return candidate;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // csv: strip markdown fences if the model added them despite instructions.
        var text = response.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        return text.Length > 0 ? text : null;
    }
}
