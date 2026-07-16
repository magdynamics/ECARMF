using System.Text.Json;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class EfTransactionStore : ITransactionStore
{
    private readonly ECARMFDbContext _db;

    public EfTransactionStore(ECARMFDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(Transaction transaction, CancellationToken ct = default)
    {
        _db.Transactions.Add(new TransactionRecord
        {
            Id = transaction.TransactionId,
            TenantId = transaction.TenantId,
            TransactionType = transaction.TransactionType,
            SubmittedBy = transaction.SubmittedBy,
            PayloadJson = JsonSerializer.Serialize(transaction.Payload),
            ReceivedAt = transaction.ReceivedAt,
            CaseId = transaction.CaseId,
            UnitRef = transaction.UnitRef
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<Transaction?> GetByIdAsync(string tenantId, Guid transactionId, CancellationToken ct = default)
    {
        var record = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == transactionId, ct);

        return record is null ? null : new Transaction
        {
            EntityId = record.Id,
            TenantId = record.TenantId,
            EntityType = nameof(Transaction),
            EntityName = record.TransactionType,
            TransactionType = record.TransactionType,
            SubmittedBy = record.SubmittedBy,
            Payload = JsonSerializer.Deserialize<Dictionary<string, string>>(record.PayloadJson) ?? [],
            ReceivedAt = record.ReceivedAt,
            CaseId = record.CaseId,
            UnitRef = record.UnitRef
        };
    }

    public async Task<IReadOnlyList<Transaction>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);

        return records.Select(ToDomain).ToList();
    }

    public async Task<(IReadOnlyList<Transaction> Items, int Total)> QueryAsync(
        TransactionQuery query, CancellationToken ct = default)
    {
        var transactions = _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == query.TenantId);

        if (!string.IsNullOrWhiteSpace(query.RecordType))
        {
            transactions = transactions.Where(t => t.TransactionType == query.RecordType);
        }
        if (!string.IsNullOrWhiteSpace(query.SubmittedBy))
        {
            transactions = transactions.Where(t => t.SubmittedBy == query.SubmittedBy);
        }
        if (!string.IsNullOrWhiteSpace(query.CaseId))
        {
            transactions = transactions.Where(t => t.CaseId == query.CaseId);
        }
        if (!string.IsNullOrWhiteSpace(query.UnitRef))
        {
            // A unit's view includes tenant-wide records (UnitRef null) unless
            // the caller asks for the unit's own data exclusively.
            transactions = query.UnitExclusive
                ? transactions.Where(t => t.UnitRef == query.UnitRef)
                : transactions.Where(t => t.UnitRef == query.UnitRef || t.UnitRef == null);
        }
        if (query.From is not null)
        {
            transactions = transactions.Where(t => t.ReceivedAt >= query.From);
        }
        if (query.To is not null)
        {
            transactions = transactions.Where(t => t.ReceivedAt <= query.To);
        }
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var term = query.Text.Trim();
            transactions = transactions.Where(t =>
                t.TransactionType.Contains(term)
                || t.SubmittedBy.Contains(term)
                || t.PayloadJson.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            // Outcome lives in the outcome stream; join by transaction id.
            transactions = transactions.Where(t => _db.TransactionOutcomes.Any(o =>
                o.TenantId == query.TenantId && o.TransactionId == t.Id && o.Outcome == query.Outcome));
        }

        var total = await transactions.CountAsync(ct);
        var records = await transactions
            .OrderByDescending(t => t.ReceivedAt)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(ct);

        return (records.Select(ToDomain).ToList(), total);
    }

    public async Task<IReadOnlyList<string>> GetRecordTypesAsync(string tenantId, CancellationToken ct = default) =>
        await _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TransactionType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

    private static Transaction ToDomain(TransactionRecord r) => new()
    {
        EntityId = r.Id,
        TenantId = r.TenantId,
        EntityType = nameof(Transaction),
        EntityName = r.TransactionType,
        TransactionType = r.TransactionType,
        SubmittedBy = r.SubmittedBy,
        Payload = JsonSerializer.Deserialize<Dictionary<string, string>>(r.PayloadJson) ?? [],
        ReceivedAt = r.ReceivedAt,
        CaseId = r.CaseId,
        UnitRef = r.UnitRef
    };
}
