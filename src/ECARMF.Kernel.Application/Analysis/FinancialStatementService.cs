using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Analysis;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Application.Analysis;

/// <summary>Tenant-scoped persistence for financial statements.</summary>
public interface IFinancialStatementStore
{
    Task<FinancialStatement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FinancialStatement>> GetAllAsync(string tenantId, string? status = null, CancellationToken ct = default);
    Task AddAsync(FinancialStatement statement, CancellationToken ct = default);
    Task UpdateAsync(FinancialStatement statement, CancellationToken ct = default);
}

public sealed record ExtractionOutcome(
    bool Success,
    FinancialStatement? Statement,
    string? Error);

public sealed record LineCorrection(string Label, decimal Value);

public interface IFinancialStatementService
{
    /// <summary>AI extraction from an unstructured PRINTED document. Every
    /// value carries the model's per-field confidence and AIGenerated
    /// provenance. If any field falls below the template's threshold the
    /// statement is gated PendingReview; otherwise it auto-approves and
    /// releases into the analysis pipeline.</summary>
    /// <param name="unitRef">Validated organizational unit the statement
    /// belongs to; null = tenant-wide. Flows onto the released record and
    /// its ratio scores.</param>
    Task<ExtractionOutcome> ExtractAsync(
        string tenantId, string templateId, string documentKind, string documentName,
        string documentText, string subjectEntity, string period, string actor,
        Guid? sourceDocumentId = null, string? unitRef = null, CancellationToken ct = default);

    /// <summary>The human decision on a gated statement. Corrections become
    /// HumanEntered at confidence 1.0. Approval releases the statement into
    /// the pipeline; rejection ends it. System/AI actors are refused — the
    /// gate exists precisely so a human stands between a misread figure and
    /// a risk score.</summary>
    Task<FinancialStatement> ReviewAsync(
        string tenantId, Guid statementId, User reviewer, bool approve,
        IReadOnlyList<LineCorrection> corrections, string? comment, CancellationToken ct = default);
}

/// <summary>
/// The AI Financial Analyst pipeline (platform-level agent). Extraction →
/// confidence gate → human review when needed → release into normal record
/// intake, where the shared package's ratio framework computes current
/// ratio, quick ratio, debt-to-equity, margins, ROA/ROE, and DSCR through
/// the SAME KpiFormulaEvaluator every tenant's KPIs use, tagged riskType
/// FinancialRisk. Outputs are risk INDICATORS for a human decision-maker —
/// never a lending, credit, or investment determination.
/// </summary>
public class FinancialStatementService : IFinancialStatementService
{
    public const string AnalyzedRecordType = "FinancialStatementAnalyzed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string ExtractionSystemPrompt =
        "You are a financial-document extraction engine. You are given the text of a PRINTED financial " +
        "statement and a list of fields to locate. Respond with ONLY a JSON object, no prose, shaped as: " +
        "{\"statementType\":\"...\",\"fields\":[{\"name\":\"...\",\"value\":123.45,\"confidence\":0.0,\"sourceText\":\"...\"}]} . " +
        "Rules: report every requested field you can find; value is the plain number (no currency symbols, " +
        "negatives as minus); confidence is YOUR certainty from 0 to 1 that the value is exactly right — be " +
        "honest, never inflate it; sourceText is the exact line you read the value from; omit fields you " +
        "cannot find rather than guessing.";

    private readonly ITenantRegistryProvider _registries;
    private readonly ILanguageModelProvider _llmProvider;
    private readonly IFinancialStatementStore _statements;
    private readonly ITransactionIntakeService _intake;
    private readonly IAuditLog _audit;

    public FinancialStatementService(
        ITenantRegistryProvider registries,
        ILanguageModelProvider llmProvider,
        IFinancialStatementStore statements,
        ITransactionIntakeService intake,
        IAuditLog audit)
    {
        _registries = registries;
        _llmProvider = llmProvider;
        _statements = statements;
        _intake = intake;
        _audit = audit;
    }

    public async Task<ExtractionOutcome> ExtractAsync(
        string tenantId, string templateId, string documentKind, string documentName,
        string documentText, string subjectEntity, string period, string actor,
        Guid? sourceDocumentId = null, string? unitRef = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
            return new ExtractionOutcome(false, null, "The document contains no text.");

        if (!_registries.GetFor(tenantId).AiExtractionTemplates.TryGet(templateId, out var registered))
            return new ExtractionOutcome(false, null,
                $"AI extraction template '{templateId}' is not registered by any active package of this tenant.");

        var template = registered.Declaration;
        if (!template.DocumentKinds.Contains(documentKind, StringComparer.OrdinalIgnoreCase))
            return new ExtractionOutcome(false, null,
                $"Template '{templateId}' accepts document kinds [{string.Join(", ", template.DocumentKinds)}]; " +
                $"'{documentKind}' is not one of them. Handwritten input ships as its own validated phase.");

        var llm = await _llmProvider.GetForTenantAsync(tenantId, ct);
        if (!llm.IsConfigured)
            return new ExtractionOutcome(false, null,
                "AI extraction needs this tenant's AI backend — configure the Anthropic API key (Setup → AI Backend).");

        string response;
        try
        {
            var fieldList = string.Join("\n", template.Fields.Select(f =>
                $"- {f.Name} ({f.DataType}{(f.Required ? ", required" : "")}): {f.Description}"));
            response = await llm.CompleteAsync(
                ExtractionSystemPrompt,
                $"Document: {documentName}\nFields to extract:\n{fieldList}\n\nDocument text:\n{documentText}",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExtractionOutcome(false, null, $"The extraction model failed: {ex.Message}");
        }

        var parsed = ParseExtraction(response);
        if (parsed is null)
            return new ExtractionOutcome(false, null, "The extraction model did not return the expected JSON shape.");

        var missingRequired = template.Fields
            .Where(f => f.Required && !parsed.Fields.Any(p => string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(f => f.Name)
            .ToList();
        if (missingRequired.Count > 0)
            return new ExtractionOutcome(false, null,
                $"The model could not find required field(s): {string.Join(", ", missingRequired)}. " +
                "The document may not be the statement type this template expects.");

        var statement = new FinancialStatement
        {
            TenantId = tenantId,
            StatementType = string.IsNullOrWhiteSpace(parsed.StatementType) ? template.TargetType : parsed.StatementType,
            SubjectEntity = subjectEntity,
            UnitRef = string.IsNullOrWhiteSpace(unitRef) ? null : unitRef.Trim(),
            Period = period,
            ExtractionMethod = ExtractionMethods.AIExtraction,
            TemplateId = templateId,
            SourceDocumentId = sourceDocumentId,
            ReviewThreshold = template.ReviewThreshold,
            CreatedBy = actor,
            LineItems = parsed.Fields.Select(f => new StatementLineItem
            {
                Label = f.Name,
                Value = f.Value,
                ConfidenceScore = Math.Clamp(f.Confidence, 0m, 1m),
                Provenance = "AIGenerated",
                SourceText = f.SourceText
            }).ToList()
        };

        var lowConfidence = statement.LowConfidenceItems.ToList();
        statement.Status = lowConfidence.Count == 0
            ? FinancialStatementStatuses.Approved
            : FinancialStatementStatuses.PendingReview;

        await _statements.AddAsync(statement, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = statement.Id,
            Category = AuditCategories.FinancialStatementExtracted,
            Actor = actor,
            Summary = $"'{documentName}' extracted as {statement.StatementType} for '{subjectEntity}' ({period}): " +
                      $"{statement.LineItems.Count} field(s), " +
                      (lowConfidence.Count == 0
                          ? "all above threshold — auto-approved."
                          : $"{lowConfidence.Count} below threshold {template.ReviewThreshold:P0} — GATED for human review " +
                            $"({string.Join(", ", lowConfidence.Select(l => l.Label))})."),
            Detail = new Dictionary<string, string>
            {
                ["statementId"] = statement.Id.ToString(),
                ["templateId"] = templateId,
                ["documentKind"] = documentKind,
                ["fieldCount"] = statement.LineItems.Count.ToString(),
                ["lowConfidenceFields"] = string.Join(", ", lowConfidence.Select(l => l.Label)),
                ["status"] = statement.Status
            }
        }, ct);

        if (statement.Status == FinancialStatementStatuses.Approved)
        {
            await ReleaseForAnalysisAsync(statement, actor, ct);
            await _statements.UpdateAsync(statement, ct);
        }

        return new ExtractionOutcome(true, statement, null);
    }

    public async Task<FinancialStatement> ReviewAsync(
        string tenantId, Guid statementId, User reviewer, bool approve,
        IReadOnlyList<LineCorrection> corrections, string? comment, CancellationToken ct = default)
    {
        if (reviewer.IsSystemActor)
            throw new InvalidOperationException(
                "An AI/system actor cannot review extracted statements — the gate exists so a HUMAN stands " +
                "between a misread figure and a risk score.");

        var statement = await _statements.GetAsync(tenantId, statementId, ct)
            ?? throw new KeyNotFoundException("Financial statement not found.");
        if (statement.Status != FinancialStatementStatuses.PendingReview)
            throw new ArgumentException($"Only a PendingReview statement can be reviewed (current: {statement.Status}).");

        foreach (var correction in corrections)
        {
            var item = statement.LineItems.FirstOrDefault(l =>
                string.Equals(l.Label, correction.Label, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                statement.LineItems.Add(new StatementLineItem
                {
                    Label = correction.Label,
                    Value = correction.Value,
                    ConfidenceScore = 1.0m,
                    Provenance = "HumanEntered",
                    SourceText = "reviewer-entered"
                });
            }
            else
            {
                item.Value = correction.Value;
                item.ConfidenceScore = 1.0m;
                item.Provenance = "HumanEntered";
            }
        }

        // Approval must leave no low-confidence value behind — validate
        // BEFORE any state mutation, so a refused approval changes nothing.
        if (approve)
        {
            var stillLow = statement.LowConfidenceItems.Select(l => l.Label).ToList();
            if (stillLow.Count > 0)
                throw new ArgumentException(
                    $"Cannot approve: field(s) still below the confidence threshold and uncorrected: " +
                    $"{string.Join(", ", stillLow)}. Correct them (or reject the statement).");
        }

        statement.Status = approve ? FinancialStatementStatuses.Approved : FinancialStatementStatuses.Rejected;
        statement.ReviewedBy = reviewer.Identifier;
        statement.ReviewedAt = DateTimeOffset.UtcNow;
        statement.ReviewComment = comment;

        if (approve)
        {
            await ReleaseForAnalysisAsync(statement, reviewer.Identifier, ct);
        }

        await _statements.UpdateAsync(statement, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = statement.Id,
            Category = AuditCategories.FinancialStatementReviewed,
            Actor = reviewer.Identifier,
            Summary = $"Statement {statement.StatementType} '{statement.SubjectEntity}' ({statement.Period}) " +
                      $"{statement.Status.ToLowerInvariant()} by {reviewer.Identifier}; " +
                      $"{corrections.Count} value(s) corrected." +
                      (string.IsNullOrWhiteSpace(comment) ? "" : $" Comment: {comment}"),
            Detail = new Dictionary<string, string>
            {
                ["statementId"] = statement.Id.ToString(),
                ["status"] = statement.Status,
                ["corrections"] = string.Join(", ", corrections.Select(c => c.Label))
            }
        }, ct);

        return statement;
    }

    /// <summary>The ONLY door into downstream analysis: an Approved
    /// statement becomes a record through normal intake, where the shared
    /// package's ratio KPIs (KpiFormulaEvaluator), deviation monitoring,
    /// and benchmarks take over — nothing bespoke.</summary>
    private async Task ReleaseForAnalysisAsync(FinancialStatement statement, string actor, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["statementId"] = statement.Id.ToString(),
            ["statementType"] = statement.StatementType,
            ["subjectEntity"] = statement.SubjectEntity,
            ["period"] = statement.Period,
            ["extractionMethod"] = statement.ExtractionMethod
        };
        foreach (var item in statement.LineItems)
        {
            payload[item.Label] = item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // The released record inherits the statement's unit, so the ratio
        // scores it produces are that unit's scores.
        await _intake.ReceiveAsync(new TransactionSubmission(
            statement.TenantId, AnalyzedRecordType, actor, payload,
            UnitRef: statement.UnitRef), ct);
        statement.AnalyzedAt = DateTimeOffset.UtcNow;
    }

    private sealed record ParsedExtraction(string? StatementType, List<ParsedField> Fields);

    private sealed record ParsedField(string Name, decimal Value, decimal Confidence, string? SourceText);

    private static ParsedExtraction? ParseExtraction(string response)
    {
        // The model was told JSON-only, but be tolerant of fenced output.
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(response[start..(end + 1)]);
            var root = doc.RootElement;
            var statementType = root.TryGetProperty("statementType", out var st) ? st.GetString() : null;
            if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                return null;

            var parsed = new List<ParsedField>();
            foreach (var field in fields.EnumerateArray())
            {
                var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!field.TryGetProperty("value", out var v) || !TryReadDecimal(v, out var value)) continue;
                var confidence = field.TryGetProperty("confidence", out var c) && TryReadDecimal(c, out var conf) ? conf : 0m;
                var sourceText = field.TryGetProperty("sourceText", out var s) ? s.GetString() : null;
                parsed.Add(new ParsedField(name, value, confidence, sourceText));
            }

            return new ParsedExtraction(statementType, parsed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetDecimal(out value);
        if (element.ValueKind == JsonValueKind.String)
            return decimal.TryParse(element.GetString(),
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value);
        return false;
    }
}
