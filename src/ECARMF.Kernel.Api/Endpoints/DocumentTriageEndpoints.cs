using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record TriageDocumentRequest(string? FileName, string? Text, string? ContentBase64);
public record DecideAllocationRequest(string? UnitRef);

/// <summary>
/// Bulk mixed-document triage: upload documents for a multi-entity group and
/// the AI recommends which business unit each belongs to; a human confirms or
/// reassigns before it is filed. AI recommends, humans decide.
/// </summary>
public static class DocumentTriageEndpoints
{
    public static IEndpointRouteBuilder MapDocumentTriageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/document-triage");

        // Analyze one document (call repeatedly for a batch). Text or base64.
        group.MapPost("/analyze", async (
            TriageDocumentRequest request, HttpContext context,
            IUserStore users, IDocumentTriageService triage, IDocumentTextReader reader, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "document.txt" : request.FileName;
            string text;
            byte[]? original = null;

            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                text = request.Text;
            }
            else if (!string.IsNullOrWhiteSpace(request.ContentBase64))
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(request.ContentBase64); }
                catch (FormatException) { return Results.BadRequest(new { error = "contentBase64 is not valid base64." }); }
                var (ok, textOrError) = reader.ReadText(fileName, bytes);
                if (!ok) return Results.BadRequest(new { error = textOrError });
                text = textOrError;
                original = bytes;
            }
            else
            {
                return Results.BadRequest(new { error = "Provide either text or contentBase64." });
            }

            var outcome = await triage.AnalyzeAsync(tenantId, fileName, text, original, user!.Identifier, ct);
            return outcome.Success
                ? Results.Ok(outcome.Allocation)
                : Results.BadRequest(new { error = outcome.Error });
        });

        // The review queue (pending by default).
        group.MapGet("/", async (
            string? status, HttpContext context, IUserStore users, IDocumentAllocationStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await store.GetByStatusAsync(tenantId, status ?? "Pending", ct));
        });

        // A human's decision: file the document under the chosen unit.
        group.MapPost("/{id:guid}/decide", async (
            Guid id, DecideAllocationRequest request, HttpContext context,
            IUserStore users, IDocumentTriageService triage, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            var outcome = await triage.DecideAsync(tenantId, id, request.UnitRef, user!, ct);
            return outcome.Success
                ? Results.Ok(outcome.Allocation)
                : Results.BadRequest(new { error = outcome.Error });
        });

        return app;
    }
}
