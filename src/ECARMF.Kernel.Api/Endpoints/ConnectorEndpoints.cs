using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public static class ConnectorEndpoints
{
    public record CreateConnectorRequest(
        string ConnectorId,
        string Name,
        string DomainTag,
        string ArrivalMode,
        string SchemaTemplateId,
        decimal ReliabilityRating,
        string ProvenanceClass);

    public record IngestRequest(string RawPayload);

    public record ExtractDocumentRequest(string? FileName, string? ContentBase64, string? Text);

    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connectors");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IConnectorStore connectors, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            await connectors.EnsureSeedConnectorsAsync(tenantId, ct);
            return Results.Ok(await connectors.GetAllAsync(tenantId, ct));
        });

        group.MapPost("/", async (
            CreateConnectorRequest request, HttpContext context,
            IUserStore users, IConnectorStore connectors, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.ConnectorId) || string.IsNullOrWhiteSpace(request.SchemaTemplateId))
                return Results.BadRequest(new { error = "connectorId and schemaTemplateId are required." });
            // The arrival mode is the FIXED small set; the domain tag is open.
            var arrivalMode = ArrivalModes.Normalize(request.ArrivalMode);
            if (!ArrivalModes.All.Contains(arrivalMode))
                return Results.BadRequest(new { error = "arrivalMode must be one of: " + string.Join(", ", ArrivalModes.All) });
            if (string.IsNullOrWhiteSpace(request.DomainTag))
                return Results.BadRequest(new { error = "domainTag is required (open tag, e.g. Banking, AccountingSystem, POS, Communications)." });
            if (await connectors.GetAsync(tenantId, request.ConnectorId, ct) is not null)
                return Results.BadRequest(new { error = $"Connector '{request.ConnectorId}' already exists." });

            var definition = new ConnectorDefinition(
                request.ConnectorId, request.Name, request.DomainTag.Trim(), arrivalMode,
                request.SchemaTemplateId, request.ReliabilityRating,
                string.IsNullOrWhiteSpace(request.ProvenanceClass) ? Provenance.ExternalSystemVerified : request.ProvenanceClass,
                "Active");

            await connectors.AddAsync(tenantId, definition, ct);
            return Results.Created($"/api/connectors/{request.ConnectorId}", definition);
        });

        // The one generic ingestion door: every connector instance —
        // manual, push, pull, file — lands here.
        group.MapPost("/{connectorId}/ingest", async (
            string connectorId, IngestRequest request, HttpContext context,
            IUserStore users, IDataSourceConnector ingestion, IConnectorStore connectors, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.RawPayload))
                return Results.BadRequest(new { error = "rawPayload is required." });

            await connectors.EnsureSeedConnectorsAsync(tenantId, ct);
            var result = await ingestion.IngestAsync(tenantId, connectorId, request.RawPayload, user!.Identifier, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // Document intake: unstructured file/text -> extraction agent ->
        // the same connector ingestion path as everything else.
        group.MapPost("/{connectorId}/extract-document", async (
            string connectorId, ExtractDocumentRequest request, HttpContext context,
            IUserStore users, IDocumentExtractor extractor, IDocumentTextReader reader,
            IConnectorStore connectors, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorIngest, ct);
            if (error is not null) return error;

            var documentName = string.IsNullOrWhiteSpace(request.FileName) ? "pasted-text" : request.FileName;
            string documentText;
            byte[]? originalContent = null;

            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                documentText = request.Text;
            }
            else if (!string.IsNullOrWhiteSpace(request.ContentBase64))
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(request.ContentBase64);
                }
                catch (FormatException)
                {
                    return Results.BadRequest(new { error = "contentBase64 is not valid base64." });
                }

                var (ok, textOrError) = reader.ReadText(documentName, bytes);
                if (!ok) return Results.BadRequest(new { error = textOrError });
                documentText = textOrError;
                originalContent = bytes; // the library archives the file as received
            }
            else
            {
                return Results.BadRequest(new { error = "Provide either text or contentBase64." });
            }

            await connectors.EnsureSeedConnectorsAsync(tenantId, ct);
            var result = await extractor.ExtractAndIngestAsync(
                tenantId, connectorId, documentName, documentText, user!.Identifier, originalContent, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return app;
    }
}
