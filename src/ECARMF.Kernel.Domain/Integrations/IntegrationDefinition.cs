namespace ECARMF.Kernel.Domain.Integrations;

/// <summary>Application categories a tenant can integrate. The list is a
/// vocabulary, not a switch statement — every type flows through the same
/// connector/template pipeline; new types are configuration.</summary>
public static class ApplicationTypes
{
    public static readonly string[] All =
    [
        "Accounting", "POS", "Billing", "RealEstateManagement",
        "Banking", "ERP", "CRM", "OperationalSystem", "Custom"
    ];
}

/// <summary>
/// A managed integration with an external application (accounting, POS,
/// billing, real-estate management, ...). The integration owns the
/// relationship — connection settings, feed mode, schedule, health — while
/// the referenced connector (and its SchemaTemplate) owns how each feed
/// payload becomes records. Push mode: the application delivers feeds to the
/// platform. Pull mode: the platform fetches from the application's export
/// endpoint, optionally on a schedule.
/// </summary>
public class IntegrationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stable identifier (slug), e.g. "quickbooks-main".</summary>
    public string IntegrationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ApplicationType { get; set; } = "Custom";

    /// <summary>The connector every feed of this integration flows through.</summary>
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>push | pull.</summary>
    public string Mode { get; set; } = "push";

    /// <summary>Pull mode: the application's export endpoint.</summary>
    public string? PullUrl { get; set; }

    /// <summary>Pull mode: scheduled interval; null = manual pulls only.</summary>
    public int? PullIntervalMinutes { get; set; }

    /// <summary>Active | Paused. Paused integrations accept no feeds.</summary>
    public string Status { get; set; } = "Active";

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Health at a glance — detail lives in the feed run history.

    public DateTimeOffset? LastFeedAt { get; set; }

    public string? LastFeedStatus { get; set; }
}

/// <summary>One feed execution — success or failure, both are history.</summary>
public class FeedRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public string IntegrationId { get; set; } = string.Empty;

    /// <summary>push | pull-manual | pull-scheduled.</summary>
    public string Trigger { get; set; } = string.Empty;

    public string TriggeredBy { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedAt { get; set; }

    public bool Success { get; set; }

    public int RecordsIngested { get; set; }

    public string? Error { get; set; }
}
