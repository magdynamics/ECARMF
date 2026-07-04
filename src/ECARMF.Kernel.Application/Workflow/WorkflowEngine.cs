using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Workflow;

namespace ECARMF.Kernel.Application.Workflow;

public interface ITaskStore
{
    Task AddAsync(TaskItem task, CancellationToken ct = default);
    Task<TaskItem?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task UpdateAsync(TaskItem task, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);
}

public interface INotificationStore
{
    Task AddAsync(NotificationItem notification, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);
}

public interface IWorkflowEngine
{
    /// <summary>Runs every registered workflow subscribed to the event whose
    /// conditions hold. AI analyzes AND acts — but only through declared,
    /// audited steps within approved package metadata.</summary>
    Task ExecuteAsync(KernelEvent kernelEvent, CancellationToken ct = default);
}

public class WorkflowEngine : IWorkflowEngine
{
    private readonly ITenantRegistryProvider _registries;
    private readonly ITaskStore _tasks;
    private readonly INotificationStore _notifications;
    private readonly IKernelEventBus _bus;
    private readonly IAuditLog _audit;

    public WorkflowEngine(
        ITenantRegistryProvider registries,
        ITaskStore tasks,
        INotificationStore notifications,
        IKernelEventBus bus,
        IAuditLog audit)
    {
        _registries = registries;
        _tasks = tasks;
        _notifications = notifications;
        _bus = bus;
        _audit = audit;
    }

    public async Task ExecuteAsync(KernelEvent kernelEvent, CancellationToken ct = default)
    {
        var registries = _registries.GetFor(kernelEvent.TenantId);
        var workflows = registries.Workflows.GetWorkflowsForEvent(kernelEvent.EventName);

        foreach (var workflow in workflows)
        {
            var matches = workflow.Declaration.Conditions.Count == 0
                || workflow.Declaration.Conditions.All(c => ConditionEvaluator.Matches(c, kernelEvent.Payload));
            if (!matches)
            {
                continue;
            }

            var executed = new List<string>();

            foreach (var step in workflow.Declaration.Steps)
            {
                var text = ReasonRenderer.Render(step.Template, kernelEvent.Payload);
                switch (step.Type.ToLowerInvariant())
                {
                    case "notify":
                        await _notifications.AddAsync(new NotificationItem
                        {
                            TenantId = kernelEvent.TenantId,
                            WorkflowId = workflow.Declaration.WorkflowId,
                            Target = step.Target,
                            Message = text,
                            Severity = step.Severity,
                            CorrelationId = kernelEvent.CorrelationId
                        }, ct);
                        executed.Add($"notify:{step.Target}");
                        break;

                    case "createtask":
                        await _tasks.AddAsync(new TaskItem
                        {
                            TenantId = kernelEvent.TenantId,
                            WorkflowId = workflow.Declaration.WorkflowId,
                            Title = text,
                            Assignee = step.Target,
                            Severity = step.Severity,
                            CorrelationId = kernelEvent.CorrelationId
                        }, ct);
                        executed.Add($"createTask:{step.Target}");
                        break;

                    case "publishevent":
                        if (registries.Events.IsDeclared(step.Target))
                        {
                            await _bus.PublishAsync(new KernelEvent(
                                kernelEvent.TenantId, step.Target, kernelEvent.CorrelationId,
                                kernelEvent.Payload, DateTimeOffset.UtcNow), ct);
                            executed.Add($"publishEvent:{step.Target}");
                        }
                        break;
                }
            }

            await _audit.AppendAsync(new AuditEntry
            {
                TenantId = kernelEvent.TenantId,
                CorrelationId = kernelEvent.CorrelationId,
                Category = AuditCategories.WorkflowExecuted,
                Actor = "system:flywheel",
                Summary = $"Workflow '{workflow.Declaration.WorkflowId}' ({workflow.PackageId} v{workflow.PackageVersion}) executed on '{kernelEvent.EventName}': {string.Join(", ", executed)}.",
                Detail = new Dictionary<string, string>
                {
                    ["workflowId"] = workflow.Declaration.WorkflowId,
                    ["packageId"] = workflow.PackageId,
                    ["packageVersion"] = workflow.PackageVersion,
                    ["eventName"] = kernelEvent.EventName,
                    ["steps"] = string.Join(", ", executed)
                }
            }, ct);
        }
    }
}
