namespace ECARMF.Kernel.Domain.Analytics;

/// <summary>
/// Raised when actual results diverge from expectation — either a Target
/// (from an OKR/KPI definition) or a Forecast (an AI-predicted value).
/// Missing data is itself a deviation: silence is a failure mode.
/// </summary>
public class DeviationAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Subject the metric belongs to (e.g. kpiId@siteId).</summary>
    public string EntityReference { get; set; } = string.Empty;

    public string MetricType { get; set; } = string.Empty;

    public decimal ActualValue { get; set; }

    public decimal ExpectedValue { get; set; }

    /// <summary>Target | Forecast | MissingData — what the expectation came from.</summary>
    public string ExpectedValueSource { get; set; } = string.Empty;

    /// <summary>Relative gap (actual - expected) / expected.</summary>
    public decimal VarianceMagnitude { get; set; }

    public decimal ThresholdBreached { get; set; }

    /// <summary>Info | Warning | Critical. Critical escalates to a human role.</summary>
    public string Severity { get; set; } = "Info";

    public Guid CorrelationId { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? AcknowledgedBy { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
