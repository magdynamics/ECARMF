using ECARMF.Kernel.Domain.Entities;

namespace ECARMF.Kernel.Domain.Identity;

/// <summary>
/// A platform identity — human or AI/system actor. System actors are
/// first-class User rows so every audit entry references a real identity,
/// never a placeholder string.
/// </summary>
public class User : UniversalBaseEntity
{
    /// <summary>Login/reference identifier (e.g. admin@platform, system:flywheel).</summary>
    public string Identifier { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>True for AI/system actors: they act under their own identity
    /// and can never self-approve an escalated or flagged outcome.</summary>
    public bool IsSystemActor { get; set; }

    /// <summary>Assigned role names, resolved against the RoleCatalog.</summary>
    public List<string> Roles { get; set; } = [];

    // Contact profile — a tenant's users are the client's people.

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? JobTitle { get; set; }

    /// <summary>True when an access key has been issued (the key itself is
    /// stored only as a hash and never returned).</summary>
    public bool HasCredential { get; set; }
}
