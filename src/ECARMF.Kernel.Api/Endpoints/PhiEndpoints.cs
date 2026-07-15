using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record PhiRevealRequest(string FieldKey, string? SubjectRef, string? Screen);

/// <summary>
/// Regulated-data reveal auditing (UI/UX Phase 1, §2.4). A PHI-badged field
/// in the UI defaults to masked; revealing it is an explicit, ATTRIBUTABLE
/// act. Every reveal is written to the append-only audit log server-side (a
/// masked field the client could quietly un-mask without a server record would
/// defeat the HIPAA access-tracking requirement), and the prior reveal is
/// returned so the UI can surface "last viewed by X at Y". Reading who last
/// viewed a field does NOT itself reveal the value.
/// </summary>
public static class PhiEndpoints
{
    public static IEndpointRouteBuilder MapPhiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/phi");

        // Record a reveal; return the PREVIOUS viewer (before this one).
        group.MapPost("/reveal", async (
            PhiRevealRequest request, HttpContext context,
            IUserStore users, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            // Viewing PHI is reading a record; require RecordRead.
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.FieldKey))
                return Results.BadRequest(new { error = "fieldKey is required." });

            var prior = await LastRevealAsync(audit, tenantId, request.FieldKey, request.SubjectRef, ct);

            var now = DateTimeOffset.UtcNow;
            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.PhiFieldRevealed,
                Actor = user!.Identifier,
                OccurredAt = now,
                Summary = $"PHI field '{request.FieldKey}'"
                    + (string.IsNullOrWhiteSpace(request.SubjectRef) ? "" : $" for '{request.SubjectRef}'")
                    + $" revealed by {user.Identifier}.",
                Detail = new Dictionary<string, string>
                {
                    ["fieldKey"] = request.FieldKey,
                    ["subjectRef"] = request.SubjectRef ?? "",
                    ["screen"] = request.Screen ?? ""
                }
            }, ct);

            return Results.Ok(new
            {
                fieldKey = request.FieldKey,
                subjectRef = request.SubjectRef,
                revealedBy = user.Identifier,
                revealedAt = now,
                previousViewedBy = prior?.Actor,
                previousViewedAt = prior?.OccurredAt
            });
        });

        // Who last viewed a field, WITHOUT revealing it (for the badge tooltip).
        group.MapGet("/last-viewed", async (
            string fieldKey, string? subjectRef, HttpContext context,
            IUserStore users, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var last = await LastRevealAsync(audit, tenantId, fieldKey, subjectRef, ct);
            return Results.Ok(new
            {
                fieldKey,
                subjectRef,
                lastViewedBy = last?.Actor,
                lastViewedAt = last?.OccurredAt
            });
        });

        return app;
    }

    /// <summary>Most recent reveal of the given field (+ optional subject) for
    /// the tenant, or null. Scans the last year of PHI-reveal audit entries.</summary>
    private static async Task<AuditEntry?> LastRevealAsync(
        IAuditLog audit, string tenantId, string fieldKey, string? subjectRef, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = await audit.GetByTimeRangeAsync(tenantId, now.AddYears(-1), now, ct);
        return entries
            .Where(e => e.Category == AuditCategories.PhiFieldRevealed
                && e.Detail.TryGetValue("fieldKey", out var fk)
                && string.Equals(fk, fieldKey, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(subjectRef)
                    || (e.Detail.TryGetValue("subjectRef", out var sr)
                        && string.Equals(sr, subjectRef, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();
    }
}
