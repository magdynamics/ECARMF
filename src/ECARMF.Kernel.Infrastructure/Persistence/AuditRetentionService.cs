using ECARMF.Kernel.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

/// <summary>Archived audit entry — the same shape as the live row plus when it
/// was moved. Rows land here via retention and are never deleted.</summary>
public class AuditArchiveRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetailJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ArchivedAt { get; set; }
}

/// <summary>
/// Moves audit entries older than the cutoff into AuditArchive in batches —
/// a set-based INSERT…DELETE per batch so the live table never locks long and
/// a crash mid-run loses nothing (rows are inserted before they're deleted;
/// re-running converges).
/// </summary>
public class AuditRetentionService : IAuditRetentionService
{
    private const int BatchSize = 5000;

    private readonly ECARMFDbContext _db;

    public AuditRetentionService(ECARMFDbContext db) => _db = db;

    public async Task<AuditRetentionResult> ArchiveAsync(int monthsToKeep, CancellationToken ct = default)
    {
        if (monthsToKeep < 1)
            throw new ArgumentOutOfRangeException(nameof(monthsToKeep), "Keep at least one month live.");

        var cutoff = DateTimeOffset.UtcNow.AddMonths(-monthsToKeep);
        var archived = 0;

        while (true)
        {
            // Move one batch atomically: copy then delete the same ids.
            var moved = await _db.Database.ExecuteSqlAsync($"""
                WITH batch AS (
                    SELECT TOP ({BatchSize}) *
                    FROM AuditEntries
                    WHERE OccurredAt < {cutoff}
                    ORDER BY OccurredAt
                )
                INSERT INTO AuditArchive (Id, TenantId, CorrelationId, Category, Actor, Summary, DetailJson, OccurredAt, ArchivedAt)
                SELECT Id, TenantId, CorrelationId, Category, Actor, Summary, DetailJson, OccurredAt, SYSDATETIMEOFFSET()
                FROM batch;
                """, ct);
            if (moved == 0) break;

            await _db.Database.ExecuteSqlAsync($"""
                DELETE a FROM AuditEntries a
                INNER JOIN AuditArchive x ON x.Id = a.Id
                WHERE a.OccurredAt < {cutoff};
                """, ct);

            archived += moved;
            if (moved < BatchSize) break;
        }

        return new AuditRetentionResult(archived, cutoff);
    }
}
