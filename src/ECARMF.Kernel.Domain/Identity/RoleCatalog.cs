namespace ECARMF.Kernel.Domain.Identity;

/// <summary>Generic permission strings. Capability-scoped permissions compose
/// with the CapabilityRegistry mechanism (Capability:{id}:{action}) instead of
/// becoming a parallel hard-coded rule set.</summary>
public static class Permissions
{
    public const string PackageManage = "Package:Manage";
    public const string RecordSubmit = "Record:Submit";
    public const string RecordRead = "Record:Read";
    public const string AuditRead = "AuditLog:Read";
    public const string ScoreRead = "Score:Read";
    public const string ScoreWrite = "Score:Write";
    public const string RegistryRead = "Registry:Read";
    public const string ConnectorConfigure = "Connector:Configure";
    public const string ConnectorIngest = "Connector:Ingest";
    public const string DualApprove = "Capability:RequireDualApproval:Approve";

    /// <summary>Wildcard: every permission. Reserved for Executive/Owner.</summary>
    public const string All = "*";
}

/// <summary>
/// The full role framework (all eight roles defined now; only Administrator,
/// Owner, and the AI system actor are seeded for the MVP demo). Roles are an
/// ordered permission list; enforcement happens at capability invocation and
/// on every write path.
/// </summary>
public static class RoleCatalog
{
    public const string PlatformAdministrator = "PlatformAdministrator";
    public const string VentureManager = "VentureManager";
    public const string TreasuryOfficer = "TreasuryOfficer";
    public const string RiskComplianceOfficer = "RiskComplianceOfficer";
    public const string Auditor = "Auditor";
    public const string ExecutiveOwner = "ExecutiveOwner";
    public const string AISystemActor = "AISystemActor";
    public const string ConnectorOwner = "ConnectorOwner";

    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Manages packages/connectors/config; cannot approve transactions
            // or make business decisions.
            [PlatformAdministrator] =
            [
                Permissions.PackageManage, Permissions.ConnectorConfigure,
                Permissions.RegistryRead, Permissions.RecordRead,
                Permissions.AuditRead, Permissions.ScoreRead
            ],
            // Submits for their ventures; cannot approve their own submissions
            // (segregation of duties enforced at the capability, not here).
            [VentureManager] =
            [
                Permissions.RecordSubmit, Permissions.RecordRead,
                Permissions.ScoreRead, Permissions.RegistryRead
            ],
            // Reviews and decides flagged records; does not originate them.
            [TreasuryOfficer] =
            [
                Permissions.DualApprove, Permissions.RecordRead,
                Permissions.ScoreRead, Permissions.AuditRead
            ],
            // Reviews scores/exceptions, proposes threshold changes as new
            // package versions; cannot directly approve treasury transactions.
            [RiskComplianceOfficer] =
            [
                Permissions.ScoreRead, Permissions.AuditRead,
                Permissions.RecordRead, Permissions.RegistryRead,
                Permissions.PackageManage
            ],
            // Strictly read-only.
            [Auditor] =
            [
                Permissions.AuditRead, Permissions.ScoreRead,
                Permissions.RecordRead, Permissions.RegistryRead
            ],
            // Intentionally the only unrestricted role.
            [ExecutiveOwner] = [Permissions.All],
            // Automated scoring/decisioning under its own identity. No
            // DualApprove: an AI actor can never self-approve an escalation.
            [AISystemActor] =
            [
                Permissions.ScoreWrite, Permissions.ScoreRead,
                Permissions.RecordRead, Permissions.ConnectorIngest
            ],
            // Owns connector credentials/health; independently revocable.
            [ConnectorOwner] = [Permissions.ConnectorConfigure, Permissions.ConnectorIngest]
        };

    public static bool HasPermission(IEnumerable<string> roles, string permission)
    {
        foreach (var role in roles)
        {
            if (!RolePermissions.TryGetValue(role, out var granted))
            {
                continue;
            }

            if (granted.Contains(Permissions.All) ||
                granted.Contains(permission, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
