namespace ECARMF.Kernel.Domain.Risk;

/// <summary>
/// The management record for a single risk: who owns it, how it's being
/// treated, and where it stands. A risk is surfaced from its risk-tagged
/// scores (the heatmap); a treatment turns that visibility into managed action
/// — an owner, a strategy, a plan, and a residual rating after mitigation.
/// </summary>
public class RiskTreatment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable identity of the risk being treated (domain + subject),
    /// so one risk has one treatment.</summary>
    public string RiskKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public int InherentSeverity { get; set; }
    public int InherentLikelihood { get; set; }

    public string? Owner { get; set; }

    /// <summary>Mitigate | Accept | Transfer | Avoid.</summary>
    public string Strategy { get; set; } = RiskStrategies.Mitigate;

    /// <summary>Identified | InTreatment | Mitigated | Accepted | Closed.</summary>
    public string Status { get; set; } = RiskTreatmentStatuses.Identified;

    public string? MitigationPlan { get; set; }

    /// <summary>Residual rating after treatment (null until assessed).</summary>
    public int? ResidualSeverity { get; set; }
    public int? ResidualLikelihood { get; set; }

    public DateTimeOffset? TargetDate { get; set; }

    /// <summary>Optional reference to a remediation action (e.g. an
    /// AutonomousActionRequest) addressing this risk.</summary>
    public string? LinkedActionRef { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class RiskStrategies
{
    public const string Mitigate = "Mitigate";
    public const string Accept = "Accept";
    public const string Transfer = "Transfer";
    public const string Avoid = "Avoid";
    public static readonly string[] All = [Mitigate, Accept, Transfer, Avoid];
    public static bool IsValid(string? s) => All.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase));
}

public static class RiskTreatmentStatuses
{
    public const string Identified = "Identified";
    public const string InTreatment = "InTreatment";
    public const string Mitigated = "Mitigated";
    public const string Accepted = "Accepted";
    public const string Closed = "Closed";
    public static readonly string[] All = [Identified, InTreatment, Mitigated, Accepted, Closed];
    public static bool IsValid(string? s) => All.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase));
}
