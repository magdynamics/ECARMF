namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// The platform serves multiple clients: every API call must identify its
/// tenant via the X-Tenant-Id header. No default tenant exists by design —
/// an unidentified request cannot touch any tenant's data.
/// </summary>
public static class TenantResolution
{
    public const string HeaderName = "X-Tenant-Id";

    public static bool TryGetTenant(HttpContext context, out string tenantId)
    {
        tenantId = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(tenantId);
    }

    public static IResult MissingTenantResult() =>
        Results.BadRequest(new { error = $"The '{HeaderName}' header is required." });
}
