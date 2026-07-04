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
}
