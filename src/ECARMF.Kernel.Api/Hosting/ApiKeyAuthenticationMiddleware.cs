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
    private readonly bool _allowHeaderIdentity;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        // Development convenience only: header-asserted identity (X-Tenant-Id
        // + X-User-Id without a credential). Set Security:AllowHeaderIdentity
        // to false when the app is shared on a network — then every /api
        // request must carry a real access key.
        _allowHeaderIdentity = configuration.GetValue("Security:AllowHeaderIdentity", true);
    }

    public async Task InvokeAsync(HttpContext context, IUserStore users, ITenantDirectory tenants)
    {
        var apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey)
            && !_allowHeaderIdentity
            && context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "An access key is required (X-Api-Key). Header-asserted identity is disabled on this deployment."
            });
            return;
        }

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

            // Sensitivity enforcement (Batch 1, Refinement 6): for
            // HighSensitivity and Regulated tenants, header-asserted
            // identity is refused even where the deployment allows it —
            // a real credential is mandatory for this tenant's data.
            if (profile is not null
                && string.IsNullOrWhiteSpace(apiKey)
                && SensitivityTiers.AtLeast(profile.SensitivityTier, SensitivityTiers.HighSensitivity)
                && context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = $"Tenant '{tenantId}' is {profile.SensitivityTier}: an access key (X-Api-Key) is required."
                });
                return;
            }
        }

        await _next(context);
    }
}
