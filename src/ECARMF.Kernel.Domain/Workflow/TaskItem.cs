namespace ECARMF.Kernel.Domain.Workflow;

/// <summary>A human work item created by an automation workflow.</summary>
public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>Role or user identifier the task is assigned to.</summary>
    public string Assignee { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    /// <summary>Open | Completed.</summary>
    public string Status { get; set; } = "Open";
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CompletedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>A message pushed to a role or user by an automation workflow.</summary>
public class NotificationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
