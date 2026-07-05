using ECARMF.Kernel.Application.Compliance;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Domain.Compliance;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Library;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveRenewalRequest(
    string Name, string Category, string? SubjectType, string? SubjectId,
    string? Counterparty, string? Reference, string? Notes,
    DateTimeOffset DueDate, int? RecurrenceMonths, int[]? LeadTimeDays,
    string NotifyRole, bool CreateTask);

public record AttachRenewalDocumentRequest(string FileName, string ContentBase64);

/// <summary>
/// Renewal commitments: dated obligations (licenses, insurance, loans,
/// leases, corporate registrations) the kernel watches so nothing lapses
/// unannounced. The monitor escalates Info → Warning → Critical as the due
/// date approaches; marking renewed advances recurring commitments a cycle.
/// </summary>
public static class RenewalEndpoints
{
    public static IEndpointRouteBuilder MapRenewalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/renewals");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            return Results.Ok(await renewals.GetAllAsync(tenantId, ct));
        });

        group.MapGet("/upcoming", async (
            int? days, HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var horizon = DateTimeOffset.UtcNow.AddDays(days is > 0 ? days.Value : 30);
            var upcoming = (await renewals.GetAllAsync(tenantId, ct))
                .Where(r => r.Status == RenewalStatuses.Active && r.DueDate <= horizon)
                .OrderBy(r => r.DueDate)
                .ToList();
            return Results.Ok(upcoming);
        });

        group.MapPost("/", async (
            SaveRenewalRequest request, HttpContext context,
            IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            // Stating obligations is configuration of the tenant's controls,
            // the same permission that governs benchmarks and connectors.
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var (valid, message, renewal) = Validate(request, tenantId, user!.Identifier);
            if (!valid) return Results.BadRequest(new { error = message });

            await renewals.AddAsync(renewal!, ct);
            return Results.Created($"/api/renewals/{renewal!.Id}", renewal);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveRenewalRequest request, HttpContext context,
            IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var existing = await renewals.GetAsync(tenantId, id, ct);
            if (existing is null) return Results.NotFound();

            var (valid, message, updated) = Validate(request, tenantId, existing.CreatedBy);
            if (!valid) return Results.BadRequest(new { error = message });

            updated!.Id = existing.Id;
            updated.CreatedAt = existing.CreatedAt;
            updated.Status = existing.Status;
            updated.RenewalCount = existing.RenewalCount;
            updated.LastRenewedAt = existing.LastRenewedAt;
            // A moved due date restarts the warning ladder.
            updated.LastAlertedThresholdDays =
                updated.DueDate == existing.DueDate ? existing.LastAlertedThresholdDays : null;
            await renewals.UpdateAsync(updated, ct);
            return Results.Ok(updated);
        });

        group.MapPost("/evaluate", async (
            HttpContext context, IUserStore users, IRenewalMonitor monitor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            // On-demand pass over this tenant's ladder — the hourly monitor
            // does the same thing platform-wide.
            var raised = await monitor.EvaluateAsync(tenantId, DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { raised });
        });

        group.MapPost("/{id:guid}/renewed", async (
            Guid id, HttpContext context, IUserStore users,
            IRenewalStore renewals, IRenewalMonitor monitor, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var renewal = await monitor.MarkRenewedAsync(tenantId, id, user!.Identifier, ct);
            return renewal is null ? Results.NotFound() : Results.Ok(renewal);
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id, HttpContext context, IUserStore users, IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var renewal = await renewals.GetAsync(tenantId, id, ct);
            if (renewal is null) return Results.NotFound();

            renewal.Status = RenewalStatuses.Cancelled;
            renewal.UpdatedAt = DateTimeOffset.UtcNow;
            await renewals.UpdateAsync(renewal, ct);
            return Results.Ok(renewal);
        });

        // Evidence for the commitment: a scan/photo of the license, the
        // policy PDF, the loan schedule. Archived in the tenant's document
        // library (hashed, indexed, immutable) and linked to the renewal.
        group.MapPost("/{id:guid}/documents", async (
            Guid id, AttachRenewalDocumentRequest request, HttpContext context,
            IUserStore users, IRenewalStore renewals, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            var renewal = await renewals.GetAsync(tenantId, id, ct);
            if (renewal is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentBase64))
                return Results.BadRequest(new { error = "fileName and contentBase64 are required." });

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.ContentBase64);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "contentBase64 is not valid base64." });
            }

            var (mediaType, contentType) = ClassifyFile(request.FileName);
            var document = await library.ArchiveAsync(new SourceDocument
            {
                TenantId = tenantId,
                FileName = request.FileName,
                MediaType = mediaType,
                SourceId = $"renewal:{renewal.Id}",
                SourceCategory = "renewal-attachment",
                UploadedBy = user!.Identifier,
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = contentType,
                    ["renewalName"] = renewal.Name,
                    ["renewalCategory"] = renewal.Category,
                    ["renewalReference"] = renewal.Reference ?? ""
                }
            }, bytes, ct);

            return Results.Created($"/api/library/{document.Id}", document);
        });

        group.MapGet("/{id:guid}/documents", async (
            Guid id, HttpContext context, IUserStore users, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await library.SearchAsync(tenantId, null, $"renewal:{id}", null, null, 100, ct));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext context, IUserStore users, ITenantDirectory tenants,
            IRenewalStore renewals, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;
            if (await SensitivityGuard.RequireDeletionAllowedAsync(tenants, tenantId, "a watched obligation", ct) is { } denied)
                return denied;

            await renewals.DeleteAsync(tenantId, id, ct);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>Library media type + HTTP content type from the file name —
    /// scans and photos of licenses/policies arrive as images or PDFs.</summary>
    private static (string MediaType, string ContentType) ClassifyFile(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => ("pdf", "application/pdf"),
            ".png" => ("image", "image/png"),
            ".jpg" or ".jpeg" => ("image", "image/jpeg"),
            ".gif" => ("image", "image/gif"),
            ".webp" => ("image", "image/webp"),
            ".bmp" => ("image", "image/bmp"),
            ".tif" or ".tiff" => ("image", "image/tiff"),
            ".json" => ("json", "application/json"),
            ".csv" => ("csv", "text/csv"),
            _ => ("text", "text/plain")
        };

    private static (bool Valid, string? Message, RenewalCommitment? Renewal) Validate(
        SaveRenewalRequest request, string tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (false, "name is required.", null);
        // Open renewal type (Batch 1, Refinement 1): CPE, COI, LeaseContract,
        // or anything a future tenant needs — the known list is suggestions,
        // never a gate.
        if (string.IsNullOrWhiteSpace(request.Category))
            return (false, "category is required (e.g. " + string.Join(", ", RenewalCategories.All) + ", CPE, COI — any type).", null);
        if (request.DueDate == default)
            return (false, "dueDate is required.", null);
        if (request.RecurrenceMonths is <= 0)
            return (false, "recurrenceMonths must be positive (or omitted for a one-time obligation).", null);
        if (!RoleCatalog.RolePermissions.ContainsKey(request.NotifyRole))
            return (false, "notifyRole must be a role from the catalog.", null);

        var ladder = (request.LeadTimeDays is { Length: > 0 } ? request.LeadTimeDays : new[] { 90, 30, 7 })
            .Where(d => d >= 0).Distinct().OrderByDescending(d => d).ToArray();
        if (ladder.Length == 0)
            return (false, "leadTimeDays must contain at least one non-negative day count.", null);

        // Normalize casing for known types; unknown types pass through as-is.
        var category = RenewalCategories.All.FirstOrDefault(c =>
            string.Equals(c, request.Category, StringComparison.OrdinalIgnoreCase))
            ?? request.Category.Trim();

        return (true, null, new RenewalCommitment
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Category = category,
            SubjectType = string.IsNullOrWhiteSpace(request.SubjectType) ? null : request.SubjectType.Trim(),
            SubjectId = string.IsNullOrWhiteSpace(request.SubjectId) ? null : request.SubjectId.Trim(),
            Counterparty = request.Counterparty,
            Reference = request.Reference,
            Notes = request.Notes,
            DueDate = request.DueDate,
            RecurrenceMonths = request.RecurrenceMonths,
            LeadTimeDays = ladder,
            NotifyRole = request.NotifyRole,
            CreateTask = request.CreateTask,
            CreatedBy = createdBy
        });
    }
}
