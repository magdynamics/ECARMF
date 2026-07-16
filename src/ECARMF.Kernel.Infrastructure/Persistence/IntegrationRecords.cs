using ECARMF.Kernel.Application.Integrations;
using ECARMF.Kernel.Domain.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class IntegrationRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string IntegrationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApplicationType { get; set; } = string.Empty;
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>Unit (legal entity/location) this integration serves; null = tenant-wide.</summary>
    public string? UnitId { get; set; }

    public string Mode { get; set; } = string.Empty;
    public string? PullUrl { get; set; }
    public int? PullIntervalMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastFeedAt { get; set; }
    public string? LastFeedStatus { get; set; }

    /// <summary>Bearer secret for pull endpoints, protected at rest.</summary>
    public string? ProtectedAuthSecret { get; set; }
}

public class FeedRunRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string IntegrationId { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Success { get; set; }
    public int RecordsIngested { get; set; }
    public string? Error { get; set; }
}

public class EfIntegrationStore : IIntegrationStore
{
    private const string ProtectorPurpose = "ECARMF.IntegrationSecrets.v1";

    private readonly ECARMFDbContext _db;
    private readonly IDataProtector _protector;

    public EfIntegrationStore(ECARMFDbContext db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public async Task<IntegrationDefinition?> GetAsync(
        string tenantId, string integrationId, CancellationToken ct = default)
    {
        var record = await _db.Integrations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.IntegrationId == integrationId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<IntegrationDefinition>> GetAllAsync(
        string tenantId, CancellationToken ct = default)
    {
        var records = await _db.Integrations.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task AddAsync(IntegrationDefinition integration, CancellationToken ct = default)
    {
        _db.Integrations.Add(new IntegrationRecord
        {
            Id = integration.Id,
            TenantId = integration.TenantId,
            IntegrationId = integration.IntegrationId,
            Name = integration.Name,
            ApplicationType = integration.ApplicationType,
            ConnectorId = integration.ConnectorId,
            UnitId = integration.UnitId,
            Mode = integration.Mode,
            PullUrl = integration.PullUrl,
            PullIntervalMinutes = integration.PullIntervalMinutes,
            Status = integration.Status,
            CreatedBy = integration.CreatedBy,
            CreatedAt = integration.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(IntegrationDefinition integration, CancellationToken ct = default)
    {
        var record = await _db.Integrations.FirstAsync(
            i => i.TenantId == integration.TenantId && i.IntegrationId == integration.IntegrationId, ct);
        record.Name = integration.Name;
        record.ApplicationType = integration.ApplicationType;
        record.ConnectorId = integration.ConnectorId;
        record.UnitId = integration.UnitId;
        record.Mode = integration.Mode;
        record.PullUrl = integration.PullUrl;
        record.PullIntervalMinutes = integration.PullIntervalMinutes;
        record.Status = integration.Status;
        record.LastFeedAt = integration.LastFeedAt;
        record.LastFeedStatus = integration.LastFeedStatus;
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRunAsync(FeedRun run, CancellationToken ct = default)
    {
        _db.FeedRuns.Add(new FeedRunRecord
        {
            Id = run.Id,
            TenantId = run.TenantId,
            IntegrationId = run.IntegrationId,
            Trigger = run.Trigger,
            TriggeredBy = run.TriggeredBy,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Success = run.Success,
            RecordsIngested = run.RecordsIngested,
            Error = run.Error
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FeedRun>> GetRunsAsync(
        string tenantId, string? integrationId, int limit, CancellationToken ct = default)
    {
        var runs = _db.FeedRuns.AsNoTracking().Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(integrationId))
        {
            runs = runs.Where(r => r.IntegrationId == integrationId);
        }

        var records = await runs.OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
        return records.Select(r => new FeedRun
        {
            Id = r.Id,
            TenantId = r.TenantId,
            IntegrationId = r.IntegrationId,
            Trigger = r.Trigger,
            TriggeredBy = r.TriggeredBy,
            StartedAt = r.StartedAt,
            FinishedAt = r.FinishedAt,
            Success = r.Success,
            RecordsIngested = r.RecordsIngested,
            Error = r.Error
        }).ToList();
    }

    public async Task<IReadOnlyList<IntegrationDefinition>> GetDueScheduledPullsAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        var candidates = await _db.Integrations.AsNoTracking()
            .Where(i => i.Status == "Active"
                        && i.Mode == "pull"
                        && i.PullIntervalMinutes != null
                        && i.PullUrl != null)
            .ToListAsync(ct);

        return candidates
            .Where(i => i.LastFeedAt is null
                        || i.LastFeedAt.Value.AddMinutes(i.PullIntervalMinutes!.Value) <= now)
            .Select(ToDomain)
            .ToList();
    }

    public async Task SetAuthSecretAsync(
        string tenantId, string integrationId, string? secret, CancellationToken ct = default)
    {
        var record = await _db.Integrations.FirstAsync(
            i => i.TenantId == tenantId && i.IntegrationId == integrationId, ct);
        record.ProtectedAuthSecret = string.IsNullOrWhiteSpace(secret) ? null : _protector.Protect(secret);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetAuthSecretAsync(
        string tenantId, string integrationId, CancellationToken ct = default)
    {
        var record = await _db.Integrations.AsNoTracking().FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.IntegrationId == integrationId, ct);
        if (record?.ProtectedAuthSecret is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(record.ProtectedAuthSecret);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IntegrationDefinition ToDomain(IntegrationRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        IntegrationId = record.IntegrationId,
        Name = record.Name,
        ApplicationType = record.ApplicationType,
        ConnectorId = record.ConnectorId,
        UnitId = record.UnitId,
        Mode = record.Mode,
        PullUrl = record.PullUrl,
        PullIntervalMinutes = record.PullIntervalMinutes,
        Status = record.Status,
        CreatedBy = record.CreatedBy,
        CreatedAt = record.CreatedAt,
        LastFeedAt = record.LastFeedAt,
        LastFeedStatus = record.LastFeedStatus
    };
}
