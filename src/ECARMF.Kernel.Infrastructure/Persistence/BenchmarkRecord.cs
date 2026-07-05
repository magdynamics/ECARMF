using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Domain.Analytics;
using ECARMF.Kernel.Domain.Packages;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class BenchmarkRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = "score";
    public string MetricType { get; set; } = string.Empty;
    public string? SubjectId { get; set; }
    public string? RecordType { get; set; }
    public string? Field { get; set; }
    public string ExpectationOperator { get; set; } = string.Empty;
    public decimal ExpectedValue { get; set; }
    public string Severity { get; set; } = "Warning";
    public string NotifyRole { get; set; } = string.Empty;
    public bool CreateTask { get; set; }
    public bool Enabled { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class EfBenchmarkStore : IBenchmarkStore
{
    private readonly ECARMFDbContext _db;

    public EfBenchmarkStore(ECARMFDbContext db) => _db = db;

    public async Task<Benchmark?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.Benchmarks.AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<Benchmark>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Benchmarks.AsNoTracking()
            .Where(b => b.TenantId == tenantId).OrderBy(b => b.Name).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Benchmark>> GetEnabledAsync(string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Benchmarks.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.Enabled).ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(Benchmark benchmark, CancellationToken ct = default)
    {
        _db.Benchmarks.Add(ToRecord(benchmark));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Benchmark benchmark, CancellationToken ct = default)
    {
        var record = await _db.Benchmarks.FirstAsync(
            b => b.TenantId == benchmark.TenantId && b.Id == benchmark.Id, ct);
        var updated = ToRecord(benchmark);
        record.Name = updated.Name;
        record.Description = updated.Description;
        record.Kind = updated.Kind;
        record.MetricType = updated.MetricType;
        record.SubjectId = updated.SubjectId;
        record.RecordType = updated.RecordType;
        record.Field = updated.Field;
        record.ExpectationOperator = updated.ExpectationOperator;
        record.ExpectedValue = updated.ExpectedValue;
        record.Severity = updated.Severity;
        record.NotifyRole = updated.NotifyRole;
        record.CreateTask = updated.CreateTask;
        record.Enabled = updated.Enabled;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var record = await _db.Benchmarks.FirstOrDefaultAsync(
            b => b.TenantId == tenantId && b.Id == id, ct);
        if (record is not null)
        {
            _db.Benchmarks.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static BenchmarkRecord ToRecord(Benchmark benchmark) => new()
    {
        Id = benchmark.Id,
        TenantId = benchmark.TenantId,
        Name = benchmark.Name,
        Description = benchmark.Description,
        Kind = benchmark.Kind,
        MetricType = benchmark.MetricType,
        SubjectId = benchmark.SubjectId,
        RecordType = benchmark.RecordType,
        Field = benchmark.Field,
        ExpectationOperator = benchmark.ExpectationOperator.ToString(),
        ExpectedValue = benchmark.ExpectedValue,
        Severity = benchmark.Severity,
        NotifyRole = benchmark.NotifyRole,
        CreateTask = benchmark.CreateTask,
        Enabled = benchmark.Enabled,
        CreatedBy = benchmark.CreatedBy,
        CreatedAt = benchmark.CreatedAt,
        UpdatedAt = benchmark.UpdatedAt
    };

    private static Benchmark ToDomain(BenchmarkRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Name = record.Name,
        Description = record.Description,
        Kind = record.Kind,
        MetricType = record.MetricType,
        SubjectId = record.SubjectId,
        RecordType = record.RecordType,
        Field = record.Field,
        ExpectationOperator = Enum.TryParse<ConditionOperator>(record.ExpectationOperator, out var op)
            ? op : ConditionOperator.LessOrEqual,
        ExpectedValue = record.ExpectedValue,
        Severity = record.Severity,
        NotifyRole = record.NotifyRole,
        CreateTask = record.CreateTask,
        Enabled = record.Enabled,
        CreatedBy = record.CreatedBy,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}
