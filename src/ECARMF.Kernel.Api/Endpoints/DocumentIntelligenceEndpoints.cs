using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Knowledge;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Knowledge;

namespace ECARMF.Kernel.Api.Endpoints;

public record ReconcileRequest(string Request);

/// <summary>
/// Document intelligence: the canonical document-type catalog every tenant
/// understands, and reconciliation tasks that answer aggregate questions over
/// extracted document data ("add all deposits in BOA account 123") — with the
/// PLATFORM doing the arithmetic and every source document shown.
/// </summary>
public static class DocumentIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapDocumentIntelligenceEndpoints(this IEndpointRouteBuilder app)
    {
        // The canonical document-type library — platform knowledge, no tenant needed.
        app.MapGet("/api/document-types", () => Results.Ok(DocumentTypeCatalog.All));

        // Run a reconciliation task.
        app.MapPost("/api/reconciliation", async (
            ReconcileRequest body, HttpContext context,
            IUserStore users, IReconciliationService reconciliation, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(body.Request))
                return Results.BadRequest(new { error = "request is required." });

            var result = await reconciliation.RunAsync(tenantId, body.Request, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }
}
