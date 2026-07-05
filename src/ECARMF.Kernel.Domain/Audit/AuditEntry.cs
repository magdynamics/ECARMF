namespace ECARMF.Kernel.Domain.Audit;

/// <summary>
/// One append-only audit fact. CorrelationId ties the entry to the
/// transaction (or package record) it concerns; Detail carries the
/// structured evidence for the entry.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TenantId { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    public string Category { get; set; } = string.Empty;

    /// <summary>The responsible identity — a real User identifier (human or
    /// AI/system actor), never a placeholder.</summary>
    public string Actor { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public Dictionary<string, string> Detail { get; set; } = [];

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Well-known audit categories written by the kernel.</summary>
public static class AuditCategories
{
    public const string RecordReceived = "RecordReceived";
    public const string EventPublished = "EventPublished";
    public const string RuleEvaluated = "RuleEvaluated";
    public const string OutcomeRecorded = "OutcomeRecorded";
    public const string ApprovalRecorded = "ApprovalRecorded";
    public const string ScoreComputed = "ScoreComputed";
    public const string AllocationRecommended = "AllocationRecommended";
    public const string AllocationDecided = "AllocationDecided";
    public const string DeviationDetected = "DeviationDetected";
    public const string WorkflowExecuted = "WorkflowExecuted";
    public const string AdvisorBriefGenerated = "AdvisorBriefGenerated";
    public const string AdvisorFeedbackRecorded = "AdvisorFeedbackRecorded";
    public const string DocumentExtracted = "DocumentExtracted";
    public const string AiSettingsUpdated = "AiSettingsUpdated";
    public const string TenantCreated = "TenantCreated";
    public const string TenantStatusChanged = "TenantStatusChanged";
    public const string UserProvisioned = "UserProvisioned";
    public const string CredentialIssued = "CredentialIssued";
    public const string UserStatusChanged = "UserStatusChanged";
    public const string IntegrationConfigured = "IntegrationConfigured";
    public const string IntegrationFeedRun = "IntegrationFeedRun";
    public const string BenchmarkBreached = "BenchmarkBreached";
    public const string BillingStatementGenerated = "BillingStatementGenerated";
    public const string BillingPlanAssigned = "BillingPlanAssigned";
    public const string AgentConsulted = "AgentConsulted";
    public const string PackageLoaded = "PackageLoaded";
    public const string PackageActivated = "PackageActivated";
    public const string PackageDeactivated = "PackageDeactivated";
    public const string PackageFailed = "PackageFailed";
    public const string RenewalAlertRaised = "RenewalAlertRaised";
    public const string RenewalCompleted = "RenewalCompleted";
    public const string NotificationEmailed = "NotificationEmailed";
    public const string MailSettingsUpdated = "MailSettingsUpdated";
    public const string ReportGenerated = "ReportGenerated";
}
