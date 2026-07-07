using ECARMF.Kernel.Application.Analysis;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Api.Endpoints;

public record ExtractStatementRequest(
    string TemplateId, string SubjectEntity, string Period,
    string? DocumentKind, string? FileName, string? ContentBase64, string? Text);

public record ReviewStatementRequest(
    string Action, List<ReviewCorrection>? Corrections, string? Comment);

public record ReviewCorrection(string Label, decimal Value);

/// <summary>
/// AI Financial Analyst endpoints. Extraction produces per-field
/// confidence; anything under the template's threshold gates the statement
/// behind HUMAN review before ratios, scores, or capital recommendations
/// can see it. Every response carries the liability framing: these are
/// statistical risk indicators for a human decision-maker — never a
/// lending, credit, or investment determination.
/// </summary>
public static class FinancialStatementEndpoints
{
    private const string Framing =
        "Risk indicator only — outputs derived from this statement are statistical indicators for a human " +
        "decision-maker, never a lending, credit, or investment determination, regardless of extraction confidence.";

    public static IEndpointRouteBuilder MapFinancialStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/financial-statements");

        // Unstructured intake: upload (base64) or paste text; the tenant's
        // model extracts the declared fields with per-field confidence.
        group.MapPost("/extract", async (
            ExtractStatementRequest request, HttpContext context,
            IUserStore users, IFinancialStatementService service,
            IDocumentTextReader reader, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.TemplateId)
                || string.IsNullOrWhiteSpace(request.SubjectEntity)
                || string.IsNullOrWhiteSpace(request.Period))
                return Results.BadRequest(new { error = "templateId, subjectEntity, and period are required." });

            var documentName = string.IsNullOrWhiteSpace(request.FileName) ? "pasted-text" : request.FileName;
            string documentText;
            Guid? sourceDocumentId = null;

            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                documentText = request.Text;
            }
            else if (!string.IsNullOrWhiteSpace(request.ContentBase64))
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(request.ContentBase64); }
                catch (FormatException) { return Results.BadRequest(new { error = "contentBase64 is not valid base64." }); }

                var (ok, textOrError) = reader.ReadText(documentName, bytes);
                if (!ok) return Results.BadRequest(new { error = textOrError });
                documentText = textOrError;

                // The ORIGINAL scan is retained for audit — the reviewer and
                // any future examiner can always see what was actually read.
                var archived = await library.ArchiveAsync(new SourceDocument
                {
                    TenantId = tenantId,
                    FileName = documentName,
                    MediaType = "document",
                    SourceId = $"financial-statement:{request.TemplateId}",
                    SourceCategory = "financial-statement-source",
                    UploadedBy = user!.Identifier,
                }, bytes, ct);
                sourceDocumentId = archived.Id;
            }
            else
            {
                return Results.BadRequest(new { error = "Provide either text or contentBase64." });
            }

            var outcome = await service.ExtractAsync(
                tenantId, request.TemplateId,
                string.IsNullOrWhiteSpace(request.DocumentKind) ? "Printed" : request.DocumentKind,
                documentName, documentText, request.SubjectEntity.Trim(), request.Period.Trim(),
                user!.Identifier, sourceDocumentId, ct);

            return outcome.Success
                ? Results.Ok(new { statement = outcome.Statement, framing = Framing })
                : Results.BadRequest(new { error = outcome.Error });
        });

        group.MapGet("/", async (
            string? status, HttpContext context,
            IUserStore users, IFinancialStatementStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var statements = await store.GetAllAsync(tenantId, status, ct);
            return Results.Ok(new { statements, framing = Framing });
        });

        group.MapGet("/{id:guid}", async (
            Guid id, HttpContext context,
            IUserStore users, IFinancialStatementStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var statement = await store.GetAsync(tenantId, id, ct);
            return statement is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    statement,
                    lowConfidenceFields = statement.LowConfidenceItems.Select(l => new
                    {
                        l.Label, l.Value, l.ConfidenceScore, l.SourceText
                    }),
                    framing = Framing
                });
        });

        // The human gate: corrections + Approve/Reject. System actors are
        // refused in the service — a human stands between a misread figure
        // and a risk score, always.
        group.MapPost("/{id:guid}/review", async (
            Guid id, ReviewStatementRequest request, HttpContext context,
            IUserStore users, IFinancialStatementService service, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var action = request.Action?.Trim();
            if (action is not ("Approve" or "Reject"))
                return Results.BadRequest(new { error = "action must be Approve or Reject." });

            try
            {
                var corrections = (request.Corrections ?? [])
                    .Select(c => new LineCorrection(c.Label, c.Value)).ToList();
                var statement = await service.ReviewAsync(
                    tenantId, id, user!, action == "Approve", corrections, request.Comment, ct);
                return Results.Ok(new { statement, framing = Framing });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
