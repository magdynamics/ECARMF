using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Application.Identity;

/// <summary>Tenant-scoped identity store. Well-known system identifiers are
/// seeded per tenant so the demo (and every audit entry) always has real
/// User rows to reference.</summary>
public interface IUserStore
{
    Task<User?> GetByIdentifierAsync(string tenantId, string identifier, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Ensures the tenant's seed identities exist: one Platform
    /// Administrator, one Executive/Owner, and the AI system actors.</summary>
    Task EnsureSeedUsersAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Resolves an identity from an access-key hash — the credential
    /// derives both the user and the tenant; headers are never trusted when a
    /// key is presented. Only Active users resolve.</summary>
    Task<User?> GetByAccessKeyHashAsync(string accessKeyHash, CancellationToken ct = default);

    /// <summary>Provisions a human user (a client contact) with an issued
    /// credential hash. Fails if the identifier already exists in the tenant.</summary>
    Task CreateUserAsync(User user, string? accessKeyHash, CancellationToken ct = default);

    /// <summary>Replaces the user's credential hash (key rotation) — the old
    /// key stops working immediately.</summary>
    Task SetAccessKeyHashAsync(string tenantId, string identifier, string accessKeyHash, CancellationToken ct = default);

    /// <summary>Active | Disabled. A disabled user cannot authenticate.</summary>
    Task SetStatusAsync(string tenantId, string identifier, string status, CancellationToken ct = default);
}

/// <summary>Well-known seed identities. Each AI agent acts under its own
/// identity so its outputs are individually attributable and trust-tracked.</summary>
public static class SeedUsers
{
    public const string Admin = "admin@platform";
    public const string Owner = "owner@platform";
    public const string SystemActor = "system:flywheel";
    public const string AdvisorActor = "system:advisor";
    public const string ExtractorActor = "system:extractor";
}
