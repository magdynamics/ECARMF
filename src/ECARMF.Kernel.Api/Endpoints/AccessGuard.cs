using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Identity + permission enforcement for every endpoint. The caller is
/// identified by the X-User-Id header (seeded identities; real auth provider
/// integration is out of MVP scope — the permission mechanism is real, the
/// credential exchange is not yet). Seed users are provisioned lazily per
/// tenant so audit entries always reference real User rows.
/// </summary>
public static class AccessGuard
{
    public const string UserHeader = "X-User-Id";

    public static async Task<(IResult? Error, User? User)> RequireAsync(
        HttpContext context,
        IUserStore users,
        string tenantId,
        string permission,
        CancellationToken ct)
    {
        var identifier = context.Request.Headers[UserHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return (Results.Json(
                new { error = $"The '{UserHeader}' header is required." }, statusCode: 401), null);
        }

        await users.EnsureSeedUsersAsync(tenantId, ct);
        var user = await users.GetByIdentifierAsync(tenantId, identifier, ct);
        if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return (Results.Json(
                new { error = $"Unknown or inactive user '{identifier}'." }, statusCode: 401), null);
        }

        if (!RoleCatalog.HasPermission(user.Roles, permission))
        {
            return (Results.Json(
                new { error = $"User '{identifier}' ({string.Join(", ", user.Roles)}) lacks permission '{permission}'." },
                statusCode: 403), null);
        }

        return (null, user);
    }
}

public static class AccessGuardExtensions
{
    /// <summary>Group-level enforcement: every route in the group requires
    /// the given permission from the authenticated seeded identity.</summary>
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, string permission)
    {
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            if (!TenantResolution.TryGetTenant(http, out var tenantId))
            {
                return TenantResolution.MissingTenantResult();
            }

            var users = http.RequestServices.GetRequiredService<IUserStore>();
            var (error, user) = await AccessGuard.RequireAsync(
                http, users, tenantId, permission, http.RequestAborted);
            if (error is not null)
            {
                return error;
            }

            http.Items["User"] = user;
            return await next(invocation);
        });

        return group;
    }
}

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Identity list for the UI role switcher; seeds the tenant's users.
        app.MapGet("/api/users", async (
            HttpContext context,
            IUserStore users,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            await users.EnsureSeedUsersAsync(tenantId, ct);
            var all = await users.GetAllAsync(tenantId, ct);
            return Results.Ok(all.Select(u => new
            {
                u.Identifier,
                u.DisplayName,
                u.IsSystemActor,
                u.Roles
            }));
        });

        return app;
    }
}
