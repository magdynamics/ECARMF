using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class TaskRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class NotificationRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class EfTaskStore : ITaskStore
{
    private readonly ECARMFDbContext _db;
    public EfTaskStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(TaskItem t, CancellationToken ct = default)
    {
        _db.Tasks.Add(new TaskRecord
        {
            Id = t.Id, TenantId = t.TenantId, WorkflowId = t.WorkflowId, Title = t.Title,
            Assignee = t.Assignee, Severity = t.Severity, Status = t.Status,
            CorrelationId = t.CorrelationId, CreatedAt = t.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TaskItem?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var r = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return r is null ? null : ToDomain(r);
    }

    public async Task UpdateAsync(TaskItem t, CancellationToken ct = default)
    {
        var r = await _db.Tasks.FirstAsync(x => x.TenantId == t.TenantId && x.Id == t.Id, ct);
        r.Status = t.Status;
        r.CompletedBy = t.CompletedBy;
        r.CompletedAt = t.CompletedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TaskItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Tasks.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).Take(limit).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    private static TaskItem ToDomain(TaskRecord r) => new()
    {
        Id = r.Id, TenantId = r.TenantId, WorkflowId = r.WorkflowId, Title = r.Title,
        Assignee = r.Assignee, Severity = r.Severity, Status = r.Status,
        CorrelationId = r.CorrelationId, CreatedAt = r.CreatedAt,
        CompletedBy = r.CompletedBy, CompletedAt = r.CompletedAt
    };
}

public class EfNotificationStore : INotificationStore
{
    private readonly ECARMFDbContext _db;
    public EfNotificationStore(ECARMFDbContext db) => _db = db;

    public async Task AddAsync(NotificationItem n, CancellationToken ct = default)
    {
        _db.Notifications.Add(new NotificationRecord
        {
            Id = n.Id, TenantId = n.TenantId, WorkflowId = n.WorkflowId, Target = n.Target,
            Message = n.Message, Severity = n.Severity,
            CorrelationId = n.CorrelationId, CreatedAt = n.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationItem>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Notifications.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).Take(limit).ToListAsync(ct);
        return records.Select(r => new NotificationItem
        {
            Id = r.Id, TenantId = r.TenantId, WorkflowId = r.WorkflowId, Target = r.Target,
            Message = r.Message, Severity = r.Severity,
            CorrelationId = r.CorrelationId, CreatedAt = r.CreatedAt
        }).ToList();
    }
}
