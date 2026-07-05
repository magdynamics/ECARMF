using System.Text.RegularExpressions;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

public record CreateTenantRequest(
    string TenantId, string Name, string? Industry, string? ContactName, string? ContactEmail, string? Notes);

public record SetTenantStatusRequest(string Status);

public record CreateTenantUserRequest(
    string Identifier, string DisplayName, string Role,
    string? Email, string? Phone, string? JobTitle);

public record SetUserStatusRequest(string Status);

/// <summary>
/// Client management for the platform operator. We run the platform for our
/// clients: tenants are onboarded here, their contacts become user profiles,
/// and every user gets an access-key credential (shown once, stored hashed).
/// Every operation requires Tenant:Manage AND the reserved 'platform' tenant —
/// a client tenant's own administrator can never manage other tenants.
/// </summary>
public static class PlatformEndpoints
{
    private static readonly Regex TenantIdShape = new("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.Compiled);

    private static Task<(IResult? Error, User? Operator)> RequirePlatformOperatorAsync(
        HttpContext context, IUserStore users, CancellationToken ct) =>
        PlatformOperator.RequireAsync(context, users, ct);

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/tenants");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, ITenantDirectory tenants, CancellationToken ct) =>
        {
            var (error, _) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;

            return Results.Ok(await tenants.GetAllAsync(ct));
        });

        group.MapPost("/", async (
            CreateTenantRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;

            var tenantId = request.TenantId?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!TenantIdShape.IsMatch(tenantId))
                return Results.BadRequest(new { error = "tenantId must be a 3-64 char lowercase slug (a-z, 0-9, hyphens)." });
            if (PlatformTenant.IsPlatform(tenantId))
                return Results.BadRequest(new { error = $"'{PlatformTenant.Id}' is the reserved operator tenant." });
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });
            if (await tenants.GetAsync(tenantId, ct) is not null)
                return Results.BadRequest(new { error = $"Tenant '{tenantId}' already exists." });

            var profile = new TenantProfile
            {
                TenantId = tenantId,
                Name = request.Name.Trim(),
                Industry = request.Industry,
                ContactName = request.ContactName,
                ContactEmail = request.ContactEmail,
                Notes = request.Notes,
                CreatedBy = op!.Identifier
            };
            await tenants.AddAsync(profile, ct);

            // Seed the tenant's well-known identities (admin/owner/AI actors)
            // so the client is operational immediately.
            await users.EnsureSeedUsersAsync(tenantId, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = profile.Id,
                Category = AuditCategories.TenantCreated,
                Actor = op.Identifier,
                Summary = $"Tenant '{profile.Name}' ({tenantId}) onboarded by the platform operator.",
                Detail = new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId,
                    ["name"] = profile.Name,
                    ["industry"] = profile.Industry ?? string.Empty,
                    ["contactEmail"] = profile.ContactEmail ?? string.Empty
                }
            }, ct);

            return Results.Created($"/api/platform/tenants/{tenantId}", profile);
        });

        group.MapPost("/{tenantId}/status", async (
            string tenantId, SetTenantStatusRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;

            if (request.Status is not (TenantStatus.Active or TenantStatus.Suspended))
                return Results.BadRequest(new { error = "status must be Active or Suspended." });

            var profile = await tenants.GetAsync(tenantId, ct);
            if (profile is null) return Results.NotFound();

            profile.Status = request.Status;
            await tenants.UpdateAsync(profile, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = profile.Id,
                Category = AuditCategories.TenantStatusChanged,
                Actor = op!.Identifier,
                Summary = $"Tenant '{tenantId}' set to {request.Status} by the platform operator.",
                Detail = new Dictionary<string, string> { ["status"] = request.Status }
            }, ct);

            return Results.Ok(profile);
        });

        group.MapGet("/{tenantId}/users", async (
            string tenantId, HttpContext context,
            IUserStore users, ITenantDirectory tenants, CancellationToken ct) =>
        {
            var (error, _) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;
            if (await tenants.GetAsync(tenantId, ct) is null) return Results.NotFound();

            var all = await users.GetAllAsync(tenantId, ct);
            return Results.Ok(all.Select(u => new
            {
                u.Identifier, u.DisplayName, u.IsSystemActor, u.Roles, u.Status,
                u.Email, u.Phone, u.JobTitle, u.HasCredential
            }));
        });

        // Provision a client contact as a user, issuing their access key.
        // The key appears ONCE in this response and is stored only as a hash.
        group.MapPost("/{tenantId}/users", async (
            string tenantId, CreateTenantUserRequest request, HttpContext context,
            IUserStore users, ITenantDirectory tenants, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;
            if (await tenants.GetAsync(tenantId, ct) is null)
                return Results.NotFound(new { error = $"Tenant '{tenantId}' is not onboarded." });

            var identifier = request.Identifier?.Trim() ?? string.Empty;
            if (identifier.Length < 3)
                return Results.BadRequest(new { error = "identifier is required (e.g. the contact's email)." });
            if (identifier.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "system actors cannot be provisioned here." });
            if (!RoleCatalog.RolePermissions.ContainsKey(request.Role ?? string.Empty)
                || string.Equals(request.Role, RoleCatalog.AISystemActor, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new
                {
                    error = "role must be one of: " + string.Join(", ",
                        RoleCatalog.RolePermissions.Keys.Where(r => r != RoleCatalog.AISystemActor))
                });
            if (await users.GetByIdentifierAsync(tenantId, identifier, ct) is not null)
                return Results.BadRequest(new { error = $"User '{identifier}' already exists in tenant '{tenantId}'." });

            var accessKey = AccessKey.Generate();
            var user = new User
            {
                TenantId = tenantId,
                Identifier = identifier,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? identifier : request.DisplayName.Trim(),
                IsSystemActor = false,
                Roles = [request.Role!],
                Email = request.Email,
                Phone = request.Phone,
                JobTitle = request.JobTitle
            };
            await users.CreateUserAsync(user, AccessKey.Hash(accessKey), ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.UserProvisioned,
                Actor = op!.Identifier,
                Summary = $"User '{identifier}' ({request.Role}) provisioned for tenant '{tenantId}' with an access credential.",
                Detail = new Dictionary<string, string>
                {
                    ["identifier"] = identifier,
                    ["role"] = request.Role!,
                    ["email"] = request.Email ?? string.Empty
                }
            }, ct);

            return Results.Created($"/api/platform/tenants/{tenantId}/users/{identifier}", new
            {
                user.Identifier, user.DisplayName, user.Roles,
                user.Email, user.Phone, user.JobTitle,
                accessKey // shown once — never retrievable again
            });
        });

        group.MapPost("/{tenantId}/users/{identifier}/rotate-key", async (
            string tenantId, string identifier, HttpContext context,
            IUserStore users, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;

            var user = await users.GetByIdentifierAsync(tenantId, identifier, ct);
            if (user is null) return Results.NotFound();
            if (user.IsSystemActor)
                return Results.BadRequest(new { error = "System actors have no access keys." });

            var accessKey = AccessKey.Generate();
            await users.SetAccessKeyHashAsync(tenantId, identifier, AccessKey.Hash(accessKey), ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.CredentialIssued,
                Actor = op!.Identifier,
                Summary = $"Access key rotated for '{identifier}' in tenant '{tenantId}'; the previous key is revoked.",
                Detail = new Dictionary<string, string> { ["identifier"] = identifier }
            }, ct);

            return Results.Ok(new { identifier, accessKey });
        });

        group.MapPost("/{tenantId}/users/{identifier}/status", async (
            string tenantId, string identifier, SetUserStatusRequest request, HttpContext context,
            IUserStore users, IAuditLog audit, CancellationToken ct) =>
        {
            var (error, op) = await RequirePlatformOperatorAsync(context, users, ct);
            if (error is not null) return error;

            if (request.Status is not ("Active" or "Disabled"))
                return Results.BadRequest(new { error = "status must be Active or Disabled." });

            var user = await users.GetByIdentifierAsync(tenantId, identifier, ct);
            if (user is null) return Results.NotFound();
            if (user.IsSystemActor)
                return Results.BadRequest(new { error = "System actors cannot be disabled here." });

            await users.SetStatusAsync(tenantId, identifier, request.Status, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid(),
                Category = AuditCategories.UserStatusChanged,
                Actor = op!.Identifier,
                Summary = $"User '{identifier}' in tenant '{tenantId}' set to {request.Status}.",
                Detail = new Dictionary<string, string> { ["identifier"] = identifier, ["status"] = request.Status }
            }, ct);

            return Results.Ok(new { identifier, request.Status });
        });

        // Who am I — resolves the authenticated identity (via access key or
        // development headers) so the UI can show the signed-in context.
        app.MapGet("/api/me", async (
            HttpContext context, IUserStore users, ITenantDirectory tenants, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var identifier = context.Request.Headers[AccessGuard.UserHeader].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                return Results.Json(new { error = "Not authenticated." }, statusCode: 401);

            await users.EnsureSeedUsersAsync(tenantId, ct);
            var user = await users.GetByIdentifierAsync(tenantId, identifier, ct);
            if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Unknown or inactive user." }, statusCode: 401);

            var profile = await tenants.GetAsync(tenantId, ct);
            return Results.Ok(new
            {
                tenantId,
                tenantName = profile?.Name ?? tenantId,
                user.Identifier,
                user.DisplayName,
                user.Roles,
                viaApiKey = context.Items.ContainsKey("AuthenticatedViaApiKey"),
                isPlatformOperator = PlatformTenant.IsPlatform(tenantId)
            });
        });

        return app;
    }
}
