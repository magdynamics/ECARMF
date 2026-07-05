namespace ECARMF.Kernel.Domain.Packages;

/// <summary>A KPI computed declaratively from record payload fields.
/// KPI calculation is a rule/formula, not a new execution mechanism.</summary>
public class KPIDefinition
{
    public string KpiId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Arithmetic over payload fields: identifiers, numbers,
    /// + - * / and parentheses (e.g. "(actualCost - plannedCost) / plannedCost").</summary>
    public string Formula { get; set; } = string.Empty;

    /// <summary>Record type whose arrival triggers evaluation.</summary>
    public string TriggerRecordType { get; set; } = string.Empty;

    /// <summary>Payload field identifying the venture/site the KPI belongs to.</summary>
    public string SubjectField { get; set; } = string.Empty;

    /// <summary>What kind of thing the subject IS (Batch 2, Refinement 7) —
    /// polymorphic like ComplianceRenewal's subject: "OrganizationalUnit"
    /// (a location, project), "User" (an employee whose KPIs tie to
    /// compensation), or any future type. Open string; stamped onto every
    /// emitted KPI score so consumers can filter by subject kind instead
    /// of guessing from the subject id's shape.</summary>
    public string SubjectType { get; set; } = "OrganizationalUnit";

    public string Unit { get; set; } = "ratio";

    public decimal? TargetValue { get; set; }

    /// <summary>higher | lower — which direction beats the target.</summary>
    public string Direction { get; set; } = "higher";
}

public class KeyResult
{
    public string KrId { get; set; } = string.Empty;
    public string KpiId { get; set; } = string.Empty;
    public decimal TargetValue { get; set; }
}

public class OKRDefinition
{
    public string OkrId { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public List<KeyResult> KeyResults { get; set; } = [];
}

/// <summary>An industry performance framework: KPIs + OKRs, shipped and
/// versioned as package metadata like everything else.</summary>
public class PerformanceFrameworkDeclaration
{
    public string FrameworkId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Industry classification used by the framework recommender.</summary>
    public string Industry { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<KPIDefinition> Kpis { get; set; } = [];

    public List<OKRDefinition> Okrs { get; set; } = [];
}
