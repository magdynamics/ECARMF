using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record BulkImportRequest(string RecordType, string FileName, string ContentBase64, string? UnitRef = null);

/// <summary>
/// Historical bulk import: a CSV whose header names the payload fields;
/// every row becomes a typed record through the standard intake — same
/// rules, scores, benchmarks, and audit as live data.
/// </summary>
public static class BulkImportEndpoints
{
    public static IEndpointRouteBuilder MapBulkImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/records/bulk-import", async (
            BulkImportRequest request, HttpContext context,
            IUserStore users, IBulkImportService import, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.RecordType))
                return Results.BadRequest(new { error = "recordType is required." });
            if (string.IsNullOrWhiteSpace(request.ContentBase64))
                return Results.BadRequest(new { error = "contentBase64 is required." });

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.ContentBase64);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "contentBase64 is not valid base64." });
            }

            try
            {
                var result = await import.ImportCsvAsync(
                    tenantId, request.RecordType.Trim(),
                    string.IsNullOrWhiteSpace(request.FileName) ? "import.csv" : request.FileName,
                    bytes, user!.Identifier,
                    string.IsNullOrWhiteSpace(request.UnitRef) ? null : request.UnitRef.Trim(), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
