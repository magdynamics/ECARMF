using ECARMF.Kernel.Domain.Risk;

namespace ECARMF.Kernel.Application.Risk;

/// <summary>
/// The remediation mechanics behind a risk treatment, extracted from the
/// endpoints so the governance-relevant logic (payload shape and the residual
/// math) is unit-testable. Spawn = a governed action that the orchestration
/// skill will DENY until approved; Resolve = the approved+verified action that
/// executes, after which the risk is mitigated to a residual below inherent.
/// </summary>
public static class RiskRemediation
{
    public const string ActionRecordType = "AutonomousActionRequest";

    /// <summary>Payload for the initial (unapproved) remediation action.
    /// riskTier High at inherent severity ≥ 4 so AO-GOV-001 applies.</summary>
    public static Dictionary<string, string> SpawnActionPayload(RiskTreatment t) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["recordType"] = ActionRecordType,
        ["actionType"] = "remediate-risk",
        ["target"] = t.Title,
        ["riskTier"] = t.InherentSeverity >= 4 ? "High" : "Medium",
        ["approved"] = "false",
        ["verified"] = "false",
        ["riskKey"] = t.RiskKey
    };

    /// <summary>Payload for the approved &amp; executed remediation action —
    /// approved+verified so the orchestration governance authorizes it.</summary>
    public static Dictionary<string, string> ResolveActionPayload(RiskTreatment t) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["recordType"] = ActionRecordType,
        ["actionType"] = "remediate-risk",
        ["target"] = t.Title,
        ["riskTier"] = t.InherentSeverity >= 4 ? "High" : "Medium",
        ["approved"] = "true",
        ["verified"] = "true",
        ["killSwitch"] = "false",
        ["riskKey"] = t.RiskKey
    };

    /// <summary>Executed remediation reduces the risk: residual sits below
    /// inherent (severity −2, likelihood −1, floored at 1) and the treatment
    /// is marked Mitigated.</summary>
    public static void ApplyMitigation(RiskTreatment t)
    {
        t.Status = RiskTreatmentStatuses.Mitigated;
        t.ResidualSeverity = Math.Max(1, t.InherentSeverity - 2);
        t.ResidualLikelihood = Math.Max(1, t.InherentLikelihood - 1);
    }
}
