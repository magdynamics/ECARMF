using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Tier-based enforcement (Batch 1, Refinement 6). Protections are read
/// from the tenant's SensitivityTier and applied automatically — never
/// ad hoc per tenant. The key-required rule for HighSensitivity+ lives in
/// the authentication middleware; the checks here cover audit visibility
/// and evidence retention.
/// </summary>
public static class SensitivityGuard
{
    private static readonly string[] OversightRoles =
        [RoleCatalog.Auditor, RoleCatalog.ExecutiveOwner, RoleCatalog.RiskComplianceOfficer];

    /// <summary>Elevated and above: the audit trail is visible only to
    /// oversight roles — an operations user of a sensitive tenant does not
    /// browse who did what.</summary>
    public static async Task<IResult?> RequireAuditVisibilityAsync(
        HttpContext context, IUserStore users, ITenantDirectory tenants,
        string tenantId, CancellationToken ct)
    {
        var profile = await tenants.GetAsync(tenantId, ct);
        if (profile is null
            || !SensitivityTiers.AtLeast(profile.SensitivityTier, SensitivityTiers.Elevated))
        {
            return null;
        }

        var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.AuditRead, ct);
        if (error is not null)
        {
            return error;
        }

        return user!.Roles.Any(r => OversightRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            ? null
            : Results.Json(new
            {
                error = $"Tenant '{tenantId}' is {profile.SensitivityTier}: the audit trail is restricted to " +
                        $"oversight roles ({string.Join(", ", OversightRoles)})."
            }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>Regulated: watched obligations are evidence — deletion is
    /// blocked; cancelling preserves the record instead.</summary>
    public static async Task<IResult?> RequireDeletionAllowedAsync(
        ITenantDirectory tenants, string tenantId, string what, CancellationToken ct)
    {
        var profile = await tenants.GetAsync(tenantId, ct);
        return profile is not null
            && SensitivityTiers.AtLeast(profile.SensitivityTier, SensitivityTiers.Regulated)
            ? Results.Json(new
            {
                error = $"Tenant '{tenantId}' is Regulated: {what} cannot be deleted — cancel it instead, " +
                        "which preserves the record for examination."
            }, statusCode: StatusCodes.Status403Forbidden)
            : null;
    }
}
