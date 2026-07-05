using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Reporting;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record GenerateReportRequest(DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd, bool Email);

/// <summary>
/// Client reports: the periodic deliverable. Archived reports live in the
/// library under sourceCategory client-report; generation is on demand
/// here and monthly by schedule.
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports");

        // Archived reports, newest first.
        group.MapGet("/", async (
            HttpContext context, IUserStore users, IDocumentLibrary library, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var docs = await library.SearchAsync(tenantId, "client-report", null, null, null, 100, ct);
            return Results.Ok(docs
                .Where(d => d.SourceCategory == "client-report")
                .OrderByDescending(d => d.ArchivedAt)
                .ToList());
        });

        // On-demand generation; default period is the month to date.
        group.MapPost("/generate", async (
            GenerateReportRequest request, HttpContext context,
            IUserStore users, IClientReportService reports, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ScoreRead, ct);
            if (error is not null) return error;

            var now = DateTimeOffset.UtcNow;
            var start = request.PeriodStart ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var end = request.PeriodEnd ?? now;
            if (end <= start)
                return Results.BadRequest(new { error = "periodEnd must be after periodStart." });

            var document = await reports.GenerateAsync(tenantId, start, end, user!.Identifier, request.Email, ct);
            return Results.Created($"/api/library/{document.Id}", document);
        });

        // Operator: run the monthly cycle for every active client now.
        app.MapPost("/api/platform/reports/run", async (
            HttpContext context, IUserStore users, IClientReportService reports, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            var generated = await reports.EnsureMonthlyReportsAsync(DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { generated });
        });

        return app;
    }
}
