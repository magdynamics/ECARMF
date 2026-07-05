namespace ECARMF.Kernel.Infrastructure.Persistence;

public class UserRecord
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Identifier { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsSystemActor { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Assigned role names as a JSON array.</summary>
    public string RolesJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    // Contact profile of the client's person.
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }

    /// <summary>SHA-256 hash of the user's access key; the key itself is
    /// shown once at issue time and never stored.</summary>
    public string? AccessKeyHash { get; set; }
}
