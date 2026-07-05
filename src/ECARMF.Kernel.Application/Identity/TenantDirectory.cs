using System.Security.Cryptography;
using ECARMF.Kernel.Domain.Tenancy;

namespace ECARMF.Kernel.Application.Identity;

/// <summary>The reserved operator tenant: the platform team acts from here
/// to onboard and manage client tenants. It is never a client itself.</summary>
public static class PlatformTenant
{
    public const string Id = "platform";

    public static bool IsPlatform(string tenantId) =>
        string.Equals(tenantId, Id, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Platform-level store of client tenant profiles.</summary>
public interface ITenantDirectory
{
    Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantProfile>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(TenantProfile profile, CancellationToken ct = default);

    Task UpdateAsync(TenantProfile profile, CancellationToken ct = default);
}

/// <summary>
/// Access-key credential helpers. A key is generated once, shown once, and
/// stored only as a SHA-256 hash — the platform can verify it but never
/// reproduce it.
/// </summary>
public static class AccessKey
{
    public const string Prefix = "ecarmf_";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Prefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant();
}
