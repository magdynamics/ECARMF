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
    /// Administrator, one Executive/Owner, and the AI system actor.</summary>
    Task EnsureSeedUsersAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>Well-known seed identities.</summary>
public static class SeedUsers
{
    public const string Admin = "admin@platform";
    public const string Owner = "owner@platform";
    public const string SystemActor = "system:flywheel";
}
