using ECARMF.Kernel.Application.Packages;

namespace ECARMF.Kernel.Tests;

/// <summary>The assertion classifier is the "value" axis of the Skills
/// Library — a misclassified control misstates what a skill protects.</summary>
public class ControlAssertionsTests
{
    [Theory]
    [InlineData("RCM-CMP-011", "Minimum necessary PHI access", null, null, "Protects privacy & PHI")]
    [InlineData("X", null, "HIPAA disclosure logging", null, "Protects privacy & PHI")]
    [InlineData("AOR-014", "Cross-tenant denial", null, "Rejected", "Prevents unauthorized or cross-tenant action")]
    [InlineData("X", "MFA required for access", null, null, "Prevents unauthorized or cross-tenant action")]
    [InlineData("AO-GOV-002", "Kill-switch blocks execution", null, null, "Prevents unauthorized or cross-tenant action")]
    [InlineData("X", "Autonomous action gate", null, null, "Governs AI autonomy")]
    [InlineData("X", null, "model drift threshold exceeded", null, "Governs AI autonomy")]
    [InlineData("FC-GOV-001", "Cash runway critically low", null, null, "Protects liquidity & financial continuity")]
    [InlineData("X", "Payment priority hold", null, null, "Protects liquidity & financial continuity")]
    [InlineData("X", "Dual approval required", null, null, "Enforces segregation of duties & approvals")]
    [InlineData("X", "Maker-checker on postings", null, null, "Enforces segregation of duties & approvals")]
    [InlineData("X", "OIG exclusion screening", null, null, "Maintains regulatory compliance")]
    [InlineData("X", null, "AML sanction list match", null, "Maintains regulatory compliance")]
    [InlineData("X", "Incident remediation overdue", null, null, "Detects & remediates risk")]
    [InlineData("X", "Anomaly detection alert", null, null, "Detects & remediates risk")]
    [InlineData("X", "Reconciliation mismatch", null, null, "Ensures data integrity & accuracy")]
    [InlineData("BR-003", "Stale critical feed", null, null, "Ensures data integrity & accuracy")]
    public void Classifies_by_the_assertion_the_control_protects(
        string id, string? name, string? description, string? outcome, string expected)
    {
        Assert.Equal(expected, ControlAssertions.Classify(id, name, description, outcome));
    }

    [Fact]
    public void Unmatched_controls_default_to_operating_policy()
    {
        Assert.Equal(ControlAssertions.Policy,
            ControlAssertions.Classify("TR-001", "Active mandate required", "generic requirement", "Rejected"));
    }

    [Fact]
    public void Privacy_wins_over_unauthorized_when_both_signals_present()
    {
        // Order of checks is deliberate: a PHI-flavored access control is a
        // privacy assertion first.
        Assert.Equal(ControlAssertions.Privacy,
            ControlAssertions.Classify("X", "PHI cross-tenant access denial", null, null));
    }

    [Fact]
    public void All_lists_every_assertion_once()
    {
        Assert.Equal(9, ControlAssertions.All.Length);
        Assert.Equal(ControlAssertions.All.Length, ControlAssertions.All.Distinct().Count());
    }
}
