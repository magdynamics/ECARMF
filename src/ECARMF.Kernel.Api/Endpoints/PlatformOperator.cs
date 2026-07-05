using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>Guard for operator-only surfaces (client management, billing):
/// requires Tenant:Manage AND the reserved 'platform' tenant — a client
/// tenant's own administrator can never reach these.</summary>
public static class PlatformOperator
{
    public static async Task<(IResult? Error, User? Operator)> RequireAsync(
        HttpContext context, IUserStore users, CancellationToken ct)
    {
        if (!TenantResolution.TryGetTenant(context, out var tenantId))
            return (TenantResolution.MissingTenantResult(), null);

        if (!PlatformTenant.IsPlatform(tenantId))
            return (Results.Json(new
            {
                error = $"This operation requires the platform-operator tenant ('{PlatformTenant.Id}')."
            }, statusCode: 403), null);

        return await AccessGuard.RequireAsync(context, users, PlatformTenant.Id, Permissions.TenantManage, ct);
    }
}
