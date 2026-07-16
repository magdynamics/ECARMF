using ECARMF.Kernel.Application.Risk;
using ECARMF.Kernel.Domain.Risk;

namespace ECARMF.Kernel.Tests;

/// <summary>The remediation mechanics are governance-relevant: the spawn
/// payload must be DENIED by the orchestration skill (unapproved) and the
/// resolve payload must pass it, and the residual math is what "mitigated"
/// means on every board pack and heatmap.</summary>
public class RiskRemediationTests
{
    private static RiskTreatment Treatment(int sev, int like) => new()
    {
        TenantId = "t", RiskKey = $"Cyber:risk-{sev}{like}", Title = "Ransomware exposure",
        Domain = "Cyber", InherentSeverity = sev, InherentLikelihood = like, CreatedBy = "tester"
    };

    [Fact]
    public void Spawn_payload_is_unapproved_and_unverified_so_governance_denies_it()
    {
        var p = RiskRemediation.SpawnActionPayload(Treatment(5, 4));
        Assert.Equal("false", p["approved"]);
        Assert.Equal("false", p["verified"]);
        Assert.Equal("High", p["riskTier"]); // sev ≥ 4 → AO-GOV-001 applies
        Assert.Equal(RiskRemediation.ActionRecordType, p["recordType"]);
        Assert.Equal("Cyber:risk-54", p["riskKey"]);
    }

    [Fact]
    public void Low_severity_spawn_is_medium_tier()
    {
        Assert.Equal("Medium", RiskRemediation.SpawnActionPayload(Treatment(3, 5))["riskTier"]);
    }

    [Fact]
    public void Resolve_payload_is_approved_verified_and_killswitch_off()
    {
        var p = RiskRemediation.ResolveActionPayload(Treatment(5, 4));
        Assert.Equal("true", p["approved"]);
        Assert.Equal("true", p["verified"]);
        Assert.Equal("false", p["killSwitch"]);
    }

    [Theory]
    [InlineData(5, 4, 3, 3)]  // the verified live case: 20 → 9
    [InlineData(4, 4, 2, 3)]
    [InlineData(2, 1, 1, 1)]  // floors at 1, never zero or negative
    [InlineData(1, 1, 1, 1)]
    public void Mitigation_reduces_to_residual_below_inherent_with_a_floor_of_one(
        int sev, int like, int expSev, int expLike)
    {
        var t = Treatment(sev, like);
        RiskRemediation.ApplyMitigation(t);
        Assert.Equal(RiskTreatmentStatuses.Mitigated, t.Status);
        Assert.Equal(expSev, t.ResidualSeverity);
        Assert.Equal(expLike, t.ResidualLikelihood);
    }

    [Theory]
    [InlineData("Mitigate", true)]
    [InlineData("accept", true)]
    [InlineData("TRANSFER", true)]
    [InlineData("Avoid", true)]
    [InlineData("Ignore", false)]
    [InlineData(null, false)]
    public void Strategy_validation_is_case_insensitive(string? strategy, bool valid) =>
        Assert.Equal(valid, RiskStrategies.IsValid(strategy));

    [Theory]
    [InlineData("Identified", true)]
    [InlineData("intreatment", true)]
    [InlineData("Mitigated", true)]
    [InlineData("Accepted", true)]
    [InlineData("Closed", true)]
    [InlineData("Done", false)]
    public void Status_validation_is_case_insensitive(string status, bool valid) =>
        Assert.Equal(valid, RiskTreatmentStatuses.IsValid(status));
}
