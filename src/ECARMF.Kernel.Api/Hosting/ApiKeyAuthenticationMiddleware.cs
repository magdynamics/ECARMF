using ECARMF.Kernel.Api.Endpoints;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Hosting;

/// <summary>
/// Credential-first authentication. When a request carries X-Api-Key, the
/// key — not the headers — determines both the user and the tenant: the
/// tenant and user headers are overwritten from the resolved identity, so
/// nothing downstream can be spoofed. Requests without a key keep the
/// header-asserted identity (development/demo mode). In both paths a
/// suspended tenant is locked out of the entire API.
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    public const string ApiKeyHeader = "X-Api-Key";

    private readonly RequestDelegate _next;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IUserStore users, ITenantDirectory tenants)
    {
        var apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var user = await users.GetByAccessKeyHashAsync(AccessKey.Hash(apiKey), context.RequestAborted);
            if (user is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or revoked access key." });
                return;
            }

            // The credential is authoritative: derive tenant and identity.
            context.Request.Headers[TenantResolution.HeaderName] = user.TenantId;
            context.Request.Headers[AccessGuard.UserHeader] = user.Identifier;
            context.Items["AuthenticatedViaApiKey"] = true;
        }

        // Tenant suspension applies to every request path once the tenant is
        // known. The reserved platform tenant can never be suspended.
        if (TenantResolution.TryGetTenant(context, out var tenantId) && !PlatformTenant.IsPlatform(tenantId))
        {
            var profile = await tenants.GetAsync(tenantId, context.RequestAborted);
            if (profile is not null && !string.Equals(profile.Status, TenantStatus.Active, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = $"Tenant '{tenantId}' is {profile.Status}. Contact the platform operator."
                });
                return;
            }
        }

        await _next(context);
    }
}
