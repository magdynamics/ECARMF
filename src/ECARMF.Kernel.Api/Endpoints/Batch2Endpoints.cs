using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Tenancy;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveITAssetRequest(
    string AssetId, string Name, string AssetType, string? OwnerUnitId,
    string? Environment, string? Notes, string? Status);

public record CreateInvestorRequest(string UserIdentifier, string? Notes);

public record InvestorVerificationRequest(
    string Check, string Status, Guid? DecisionId, string? Comment);

/// <summary>
/// Batch 2 endpoints: the IT asset inventory (Refinement 9 — expirations
/// ride ComplianceRenewal, posture rides riskType-tagged scores) and
/// investor identity gating (Refinement 10 — KYC/AML/accreditation as
/// accept/reject decisions by a human with decision authority, never
/// calendar renewals).
/// </summary>
public static class Batch2Endpoints
{
    public static IEndpointRouteBuilder MapBatch2Endpoints(this IEndpointRouteBuilder app)
    {
        var assets = app.MapGroup("/api/it-assets");

        assets.MapGet("/", async (
            HttpContext context, IUserStore users, IITAssetStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RegistryRead, ct);
            if (error is not null) return error;

            return Results.Ok(await store.GetAllAsync(tenantId, ct));
        });

        assets.MapPost("/", async (
            SaveITAssetRequest request, HttpContext context,
            IUserStore users, IITAssetStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.AssetId) || string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.AssetType))
                return Results.BadRequest(new { error = "assetId, name, and assetType are required (assetType is open: Server, CloudResource, License, Certificate, ...)." });
            if (await store.GetAsync(tenantId, request.AssetId.Trim().ToLowerInvariant(), ct) is not null)
                return Results.BadRequest(new { error = $"IT asset '{request.AssetId}' already exists." });

            var asset = new ITAsset
            {
                TenantId = tenantId,
                AssetId = request.AssetId.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                AssetType = request.AssetType.Trim(),
                OwnerUnitId = request.OwnerUnitId,
                Environment = request.Environment,
                Notes = request.Notes,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status,
                CreatedBy = user!.Identifier
            };
            await store.AddAsync(asset, ct);
            return Results.Created($"/api/it-assets/{asset.AssetId}", asset);
        });

        var investors = app.MapGroup("/api/investors");

        investors.MapGet("/", async (
            HttpContext context, IUserStore users, IInvestorProfileStore store, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var profiles = await store.GetAllAsync(tenantId, ct);
            return Results.Ok(profiles.Select(p => new
            {
                p.UserIdentifier, p.KycStatus, p.AmlStatus, p.AccreditationStatus,
                p.OnboardingDecisionId, cleared = p.IsCleared, p.Notes, p.CreatedAt, p.UpdatedAt
            }));
        });

        investors.MapPost("/", async (
            CreateInvestorRequest request, HttpContext context,
            IUserStore users, IInvestorProfileStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.UserIdentifier))
                return Results.BadRequest(new { error = "userIdentifier is required." });
            if (await users.GetByIdentifierAsync(tenantId, request.UserIdentifier.Trim(), ct) is null)
                return Results.BadRequest(new { error = $"User '{request.UserIdentifier}' does not exist in this tenant — provision the identity first." });
            if (await store.GetAsync(tenantId, request.UserIdentifier.Trim(), ct) is not null)
                return Results.BadRequest(new { error = "An investor profile already exists for this user." });

            var profile = new InvestorProfile
            {
                TenantId = tenantId,
                UserIdentifier = request.UserIdentifier.Trim(),
                Notes = request.Notes,
                CreatedBy = user!.Identifier
            };
            await store.AddAsync(profile, ct);
            return Results.Created($"/api/investors/{profile.UserIdentifier}", profile);
        });

        // The gating decision: verify/reject a check. Dual-approval
        // authority required — the same segregation as every other
        // accept/reject outcome; an AI actor is structurally refused.
        investors.MapPost("/{identifier}/verification", async (
            string identifier, InvestorVerificationRequest request, HttpContext context,
            IUserStore users, IInvestorProfileStore store, IAuditLog audit, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;
            if (user!.IsSystemActor)
                return Results.BadRequest(new { error = "An AI/system actor cannot decide investor verification." });

            var profile = await store.GetAsync(tenantId, identifier, ct);
            if (profile is null) return Results.NotFound();

            var check = request.Check?.Trim().ToLowerInvariant();
            if (check is not (InvestorChecks.Kyc or InvestorChecks.Aml or InvestorChecks.Accreditation))
                return Results.BadRequest(new { error = "check must be kyc, aml, or accreditation." });
            var status = InvestorCheckStatuses.All.FirstOrDefault(s =>
                string.Equals(s, request.Status, StringComparison.OrdinalIgnoreCase));
            if (status is null)
                return Results.BadRequest(new { error = "status must be Pending, Verified, or Rejected." });

            switch (check)
            {
                case InvestorChecks.Kyc: profile.KycStatus = status; break;
                case InvestorChecks.Aml: profile.AmlStatus = status; break;
                default: profile.AccreditationStatus = status; break;
            }
            if (request.DecisionId is { } decisionId)
            {
                profile.OnboardingDecisionId = decisionId;
            }
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await store.UpdateAsync(profile, ct);

            await audit.AppendAsync(new AuditEntry
            {
                TenantId = tenantId,
                CorrelationId = profile.Id,
                Category = AuditCategories.InvestorVerificationDecided,
                Actor = user.Identifier,
                Summary = $"Investor '{identifier}' {check.ToUpperInvariant()} set to {status} by {user.Identifier}" +
                          (profile.IsCleared ? " — all checks Verified; investor is CLEARED." : "."),
                Detail = new Dictionary<string, string>
                {
                    ["userIdentifier"] = identifier,
                    ["check"] = check,
                    ["status"] = status,
                    ["decisionId"] = request.DecisionId?.ToString() ?? "",
                    ["comment"] = request.Comment ?? "",
                    ["cleared"] = profile.IsCleared.ToString()
                }
            }, ct);

            return Results.Ok(new
            {
                profile.UserIdentifier, profile.KycStatus, profile.AmlStatus,
                profile.AccreditationStatus, profile.OnboardingDecisionId, cleared = profile.IsCleared
            });
        });

        return app;
    }
}
