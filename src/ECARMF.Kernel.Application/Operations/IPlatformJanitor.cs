namespace ECARMF.Kernel.Application.Operations;

/// <summary>
/// Platform-operator cleanup for GHOST tenants: a tenant id that was never
/// onboarded (no TenantProfile) but accumulated seeded rows — users,
/// connectors — because a screen viewed it before the ghost-tenant guard
/// existed. Real onboarded clients are NEVER purged through this; they are
/// suspended, keeping their records examinable.
/// </summary>
public interface IPlatformJanitor
{
    /// <summary>Deletes every row carrying the ghost tenant id across all
    /// tenant-scoped tables. Returns per-table deletion counts.</summary>
    Task<IReadOnlyDictionary<string, int>> PurgeGhostTenantAsync(string tenantId, CancellationToken ct = default);
}
