using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Onboarding;

namespace ECARMF.Kernel.Api.Endpoints;

public record CaptureTemplateRequest(
    string TemplateId, string Name, string? Industry, string? Description, string FromTenantId);

public record ApplyTemplateRequest(string TenantId);

/// <summary>
/// Industry starter packs (operator console): capture a well-configured
/// tenant as a template, apply it to new clients in one click.
/// </summary>
public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/templates");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IOnboardingTemplateService templates, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            return Results.Ok(await templates.GetAllAsync(ct));
        });

        group.MapPost("/capture", async (
            CaptureTemplateRequest request, HttpContext context,
            IUserStore users, IOnboardingTemplateService templates, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.TemplateId) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "templateId and name are required." });
            if (string.IsNullOrWhiteSpace(request.FromTenantId))
                return Results.BadRequest(new { error = "fromTenantId is required." });

            var summary = await templates.CaptureAsync(
                request.TemplateId.Trim().ToLowerInvariant(), request.Name.Trim(),
                request.Industry, request.Description, request.FromTenantId.Trim(),
                op!.Identifier, ct);
            return Results.Ok(summary);
        });

        group.MapPost("/{templateId}/apply", async (
            string templateId, ApplyTemplateRequest request, HttpContext context,
            IUserStore users, IOnboardingTemplateService templates, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.TenantId))
                return Results.BadRequest(new { error = "tenantId is required." });

            try
            {
                var result = await templates.ApplyAsync(templateId, request.TenantId.Trim(), op!.Identifier, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        group.MapDelete("/{templateId}", async (
            string templateId, HttpContext context,
            IUserStore users, IOnboardingTemplateService templates, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            await templates.DeleteAsync(templateId, ct);
            return Results.NoContent();
        });

        return app;
    }
}
