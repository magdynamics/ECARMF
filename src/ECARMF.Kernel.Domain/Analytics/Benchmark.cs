using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Domain.Analytics;

/// <summary>
/// A tenant-set expectation with a trigger: "GP% must stay at or above 25%",
/// "no single bank movement above 10,000", "open violations must not exceed
/// 10". Benchmarks are runtime configuration (like dashboards, deliberately
/// not package-versioned): the tenant states the expectation, chooses the
/// severity and who gets alerted, and the kernel raises the alarm whenever an
/// observed value breaks it.
/// </summary>
public class Benchmark
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>score — watches every ScoreRecord of MetricType (KPIs, ratings,
    /// counts); recordField — watches a payload field of incoming records.</summary>
    public string Kind { get; set; } = "score";

    /// <summary>The ScoreType watched (kind=score), e.g. GPPercent, AMLRisk, KPIActual.</summary>
    public string MetricType { get; set; } = string.Empty;

    /// <summary>Optional subject filter (kind=score), e.g. one venture or KPI subject.</summary>
    public string? SubjectId { get; set; }

    /// <summary>The record type watched (kind=recordField), e.g. withdrawal, JournalEntry.</summary>
    public string? RecordType { get; set; }

    /// <summary>The numeric payload field watched (kind=recordField), e.g. amount.</summary>
    public string? Field { get; set; }

    /// <summary>The expectation that must HOLD: observed [operator] expected.
    /// A breach is any observation where it does not hold.</summary>
    public ConditionOperator ExpectationOperator { get; set; } = ConditionOperator.LessOrEqual;

    public decimal ExpectedValue { get; set; }

    /// <summary>Info | Warning | Critical.</summary>
    public string Severity { get; set; } = "Warning";

    /// <summary>Role notified on breach (e.g. ExecutiveOwner, RiskComplianceOfficer).</summary>
    public string NotifyRole { get; set; } = "ExecutiveOwner";

    /// <summary>Also open a review task on breach.</summary>
    public bool CreateTask { get; set; }

    public bool Enabled { get; set; } = true;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
