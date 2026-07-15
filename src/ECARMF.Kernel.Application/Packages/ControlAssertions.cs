namespace ECARMF.Kernel.Application.Packages;

/// <summary>
/// The "assertion" a control protects — the objective or risk it is there to
/// defend. Controls (executable rules) are classified into a small, stable set
/// so a skill's value can be expressed as "which assertions, and how many
/// controls, it covers". Classification is keyword-based and code-defined, the
/// same lightweight approach as skill tiers; it can be curated later.
/// </summary>
public static class ControlAssertions
{
    public const string Unauthorized = "Prevents unauthorized or cross-tenant action";
    public const string Liquidity = "Protects liquidity & financial continuity";
    public const string Integrity = "Ensures data integrity & accuracy";
    public const string Segregation = "Enforces segregation of duties & approvals";
    public const string Compliance = "Maintains regulatory compliance";
    public const string Privacy = "Protects privacy & PHI";
    public const string RiskRemediation = "Detects & remediates risk";
    public const string AiGovernance = "Governs AI autonomy";
    public const string Policy = "Enforces operating policy";

    /// <summary>All assertions in a stable display order.</summary>
    public static readonly string[] All =
    [
        Unauthorized, Liquidity, Integrity, Segregation, Compliance,
        Privacy, RiskRemediation, AiGovernance, Policy
    ];

    public static string Classify(string controlId, string? name, string? description, string? outcome)
    {
        var s = $"{controlId} {name} {description} {outcome}".ToLowerInvariant();

        if (Has(s, "phi", "hipaa", "privacy", "minimum-necessary", "minimum necessary", "consent", "personal data"))
            return Privacy;
        if (Has(s, "cross-tenant", "cross tenant", "unauthorized", "authoriz", "kill-switch", "kill switch", "mfa", "access boundary"))
            return Unauthorized;
        if (Has(s, "ai-boundary", "ai boundary", "autonomous", "model drift", "agent", "human-in", "human in the loop"))
            return AiGovernance;
        if (Has(s, "liquidity", "continuity", "cash", "funding", "reroute", "payment priority", "runway", "covenant"))
            return Liquidity;
        if (Has(s, "segregation", "dual", "maker", "checker", "four-eyes", "four eyes", "approval", "authorization limit"))
            return Segregation;
        if (Has(s, "compliance", "regulat", "obligation", "oig", "sanction", "aml", "kyc", "filing", "disclosure"))
            return Compliance;
        if (Has(s, "drift", "anomal", "incident", "remediat", "breach", "deviation", "exception", "alert"))
            return RiskRemediation;
        if (Has(s, "reconcil", "integrity", "mismatch", "validation", "accuracy", "stale", "duplicate", "completeness"))
            return Integrity;

        return Policy;
    }

    private static bool Has(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
