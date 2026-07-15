namespace ECARMF.Kernel.Domain.Cases;

/// <summary>
/// A case (or project): a named, cross-cutting grouping of records within a
/// tenant. Records from any skill can be filed under a case, so a case can be
/// monitored and compared against other cases with the full set of controls
/// and KPIs applied. The kernel treats a case as a label, not a data silo.
/// </summary>
public class Case
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable slug used on records (e.g. "acme-onboarding").</summary>
    public string CaseId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Open | Closed.</summary>
    public string Status { get; set; } = CaseStatuses.Open;

    /// <summary>Skills (package ids) the operator considers relevant to this
    /// case. Advisory — controls run regardless of this list.</summary>
    public List<string> Skills { get; set; } = [];

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class CaseStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    public static bool IsValid(string? s) =>
        string.Equals(s, Open, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(s, Closed, System.StringComparison.OrdinalIgnoreCase);
}
