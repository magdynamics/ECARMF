using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Tests;

/// <summary>Role framework enforcement: the permission catalog is the single
/// enforcement source for capability invocation.</summary>
public class IdentityTests
{
    [Fact]
    public void AI_system_actor_can_never_hold_dual_approval()
    {
        // An AI actor must never self-approve an escalated/flagged outcome.
        Assert.False(RoleCatalog.HasPermission([RoleCatalog.AISystemActor], Permissions.DualApprove));
        Assert.True(RoleCatalog.HasPermission([RoleCatalog.AISystemActor], Permissions.ScoreWrite));
    }

    [Fact]
    public void Platform_administrator_manages_packages_but_cannot_approve()
    {
        Assert.True(RoleCatalog.HasPermission([RoleCatalog.PlatformAdministrator], Permissions.PackageManage));
        Assert.False(RoleCatalog.HasPermission([RoleCatalog.PlatformAdministrator], Permissions.DualApprove));
        Assert.False(RoleCatalog.HasPermission([RoleCatalog.PlatformAdministrator], Permissions.RecordSubmit));
    }

    [Fact]
    public void Auditor_is_strictly_read_only()
    {
        string[] auditor = [RoleCatalog.Auditor];
        Assert.True(RoleCatalog.HasPermission(auditor, Permissions.AuditRead));
        Assert.True(RoleCatalog.HasPermission(auditor, Permissions.ScoreRead));
        Assert.False(RoleCatalog.HasPermission(auditor, Permissions.RecordSubmit));
        Assert.False(RoleCatalog.HasPermission(auditor, Permissions.DualApprove));
        Assert.False(RoleCatalog.HasPermission(auditor, Permissions.PackageManage));
        Assert.False(RoleCatalog.HasPermission(auditor, Permissions.ConnectorConfigure));
    }

    [Fact]
    public void Owner_is_the_only_unrestricted_role()
    {
        foreach (var permission in new[]
        {
            Permissions.PackageManage, Permissions.RecordSubmit, Permissions.DualApprove,
            Permissions.AuditRead, Permissions.ConnectorConfigure, Permissions.ScoreWrite
        })
        {
            Assert.True(RoleCatalog.HasPermission([RoleCatalog.ExecutiveOwner], permission));
        }
    }

    [Fact]
    public void Treasury_officer_approves_but_does_not_originate()
    {
        Assert.True(RoleCatalog.HasPermission([RoleCatalog.TreasuryOfficer], Permissions.DualApprove));
        Assert.False(RoleCatalog.HasPermission([RoleCatalog.TreasuryOfficer], Permissions.RecordSubmit));
    }

    [Fact]
    public void Unknown_role_grants_nothing()
    {
        Assert.False(RoleCatalog.HasPermission(["MadeUpRole"], Permissions.AuditRead));
        Assert.False(RoleCatalog.HasPermission([], Permissions.AuditRead));
    }
}
