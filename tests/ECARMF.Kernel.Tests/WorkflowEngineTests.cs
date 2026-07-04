using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Packages;
using ECARMF.Kernel.Domain.Workflow;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class InMemoryTaskStore : ITaskStore
{
    public List<TaskItem> Items { get; } = [];
    public Task AddAsync(TaskItem task, CancellationToken ct = default) { Items.Add(task); return Task.CompletedTask; }
    public Task<TaskItem?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t => t.TenantId == tenantId && t.Id == id));
    public Task UpdateAsync(TaskItem task, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<TaskItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(t => t.TenantId == tenantId).ToList());
}

public class InMemoryNotificationStore : INotificationStore
{
    public List<NotificationItem> Items { get; } = [];
    public Task AddAsync(NotificationItem n, CancellationToken ct = default) { Items.Add(n); return Task.CompletedTask; }
    public Task<IReadOnlyList<NotificationItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotificationItem>>(Items.Where(n => n.TenantId == tenantId).ToList());
}

/// <summary>Automation workflows: declarative trigger -> steps, executed by
/// the kernel, every execution audited.</summary>
public class WorkflowEngineTests
{
    private const string Tenant = "tenant-a";

    private readonly TenantRegistryProvider _registries = new();
    private readonly InMemoryTaskStore _tasks = new();
    private readonly InMemoryNotificationStore _notifications = new();
    private readonly InProcessKernelEventBus _bus = new();
    private readonly InMemoryAuditLog _audit = new();

    private WorkflowEngine CreateEngine() => new(_registries, _tasks, _notifications, _bus, _audit);

    private void RegisterWorkflow(WorkflowDeclaration workflow) =>
        _registries.GetFor(Tenant).Workflows.Register(workflow, "pkg.wf", "1.0.0");

    private static KernelEvent Event(string name, Dictionary<string, string>? payload = null) =>
        new(Tenant, name, Guid.NewGuid(),
            payload ?? new Dictionary<string, string> { ["amount"] = "90000", ["ventureId"] = "V-001" },
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Matching_workflow_notifies_creates_task_and_is_audited()
    {
        RegisterWorkflow(new WorkflowDeclaration
        {
            WorkflowId = "WF-1",
            TriggerEvent = "Flagged",
            Steps =
            [
                new WorkflowStep { Type = "notify", Target = "TreasuryOfficer", Template = "Withdrawal {amount} flagged.", Severity = "Warning" },
                new WorkflowStep { Type = "createTask", Target = "TreasuryOfficer", Template = "Review withdrawal {amount}", Severity = "Warning" }
            ]
        });
        var kernelEvent = Event("Flagged");

        await CreateEngine().ExecuteAsync(kernelEvent);

        var notification = Assert.Single(_notifications.Items);
        Assert.Equal("Withdrawal 90000 flagged.", notification.Message);
        Assert.Equal("TreasuryOfficer", notification.Target);
        var task = Assert.Single(_tasks.Items);
        Assert.Equal("Review withdrawal 90000", task.Title);
        Assert.Equal("Open", task.Status);
        Assert.Equal(kernelEvent.CorrelationId, task.CorrelationId);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.WorkflowExecuted);
    }

    [Fact]
    public async Task Conditions_gate_execution()
    {
        RegisterWorkflow(new WorkflowDeclaration
        {
            WorkflowId = "WF-2",
            TriggerEvent = "Flagged",
            Conditions = [new RuleCondition { Field = "amount", Operator = ConditionOperator.GreaterThan, Value = "100000" }],
            Steps = [new WorkflowStep { Type = "notify", Target = "Owner", Template = "big" }]
        });

        await CreateEngine().ExecuteAsync(Event("Flagged")); // amount 90000 < 100000

        Assert.Empty(_notifications.Items);
    }

    [Fact]
    public async Task PublishEvent_step_requires_a_declared_event()
    {
        _registries.GetFor(Tenant).Events.Register(
            new EventDeclaration { EventName = "InvestigationOpened" }, "pkg.wf", "1.0.0");
        RegisterWorkflow(new WorkflowDeclaration
        {
            WorkflowId = "WF-3",
            TriggerEvent = "Flagged",
            Steps =
            [
                new WorkflowStep { Type = "publishEvent", Target = "InvestigationOpened", Template = "" },
                new WorkflowStep { Type = "publishEvent", Target = "NotDeclared", Template = "" }
            ]
        });

        await CreateEngine().ExecuteAsync(Event("Flagged"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var enumerator = _bus.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("InvestigationOpened", enumerator.Current.EventName);
        var audit = _audit.Items.Single(a => a.Category == AuditCategories.WorkflowExecuted);
        // The undeclared event was skipped, not published blindly.
        Assert.DoesNotContain("NotDeclared", audit.Detail["steps"]);
    }
}
