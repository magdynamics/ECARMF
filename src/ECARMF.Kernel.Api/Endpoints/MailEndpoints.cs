using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Notifications;
using ECARMF.Kernel.Domain.Audit;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveMailSettingsRequest(
    bool Enabled, string Host, int Port, bool UseSsl,
    string? Username, string? Password, string FromAddress, string MinSeverity);

public record TestMailRequest(string To);

/// <summary>
/// Platform mail delivery (operator console): point the platform at an SMTP
/// server and alarms leave the app — benchmark breaches and renewal
/// warnings reach the client's inbox, not just the notification bell.
/// </summary>
public static class MailEndpoints
{
    public static IEndpointRouteBuilder MapMailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/mail");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IMailSettingsStore store, CancellationToken ct) =>
        {
            var (error, _) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            return Results.Ok(await store.GetStatusAsync(ct));
        });

        group.MapPut("/", async (
            SaveMailSettingsRequest request, HttpContext context,
            IUserStore users, IMailSettingsStore store, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.Host))
                return Results.BadRequest(new { error = "host is required." });
            if (request.Port is < 1 or > 65535)
                return Results.BadRequest(new { error = "port must be 1-65535." });
            if (string.IsNullOrWhiteSpace(request.FromAddress) || !request.FromAddress.Contains('@'))
                return Results.BadRequest(new { error = "fromAddress must be a valid email address." });
            if (request.MinSeverity is not ("Info" or "Warning" or "Critical"))
                return Results.BadRequest(new { error = "minSeverity must be Info, Warning, or Critical." });

            await store.SetAsync(new MailDeliverySettings(
                request.Enabled, request.Host.Trim(), request.Port, request.UseSsl,
                string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
                request.Password, request.FromAddress.Trim(), request.MinSeverity),
                op!.Identifier, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = PlatformTenant.Id,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.MailSettingsUpdated,
                Actor = op.Identifier,
                Summary = $"Mail delivery settings updated: {request.Host}:{request.Port}, " +
                          $"{(request.Enabled ? "enabled" : "disabled")}, min severity {request.MinSeverity}.",
                Detail = new Dictionary<string, string>
                {
                    ["host"] = request.Host,
                    ["port"] = request.Port.ToString(),
                    ["enabled"] = request.Enabled.ToString(),
                    ["minSeverity"] = request.MinSeverity
                }
            }, ct);

            return Results.Ok(await store.GetStatusAsync(ct));
        });

        // Proof before trust: send a test message through the configured
        // server so the operator knows delivery works before an alarm needs it.
        group.MapPost("/test", async (
            TestMailRequest request, HttpContext context,
            IUserStore users, IMailSettingsStore store, IEmailSender sender, CancellationToken ct) =>
        {
            var (error, op) = await PlatformOperator.RequireAsync(context, users, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@'))
                return Results.BadRequest(new { error = "to must be a valid email address." });

            var settings = await store.GetAsync(ct);
            if (settings is null)
                return Results.BadRequest(new { error = "Configure mail settings first." });

            try
            {
                await sender.SendAsync(settings, new EmailMessage(
                    new[] { request.To.Trim() },
                    "[ECARMF] Test message",
                    $"Mail delivery from the ECARMF platform works.\r\n\r\n" +
                    $"Sent by {op!.Identifier} at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC " +
                    $"via {settings.Host}:{settings.Port}."), ct);
                return Results.Ok(new { sent = true, to = request.To.Trim() });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Send failed: {ex.Message}" });
            }
        });

        return app;
    }
}
