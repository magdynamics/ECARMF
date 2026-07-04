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
            ReceivedAt = transaction.ReceivedAt
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Transaction>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var records = await _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);

        return records.Select(r => new Transaction
        {
            EntityId = r.Id,
            TenantId = r.TenantId,
            EntityType = nameof(Transaction),
            EntityName = r.TransactionType,
            TransactionType = r.TransactionType,
            SubmittedBy = r.SubmittedBy,
            Payload = JsonSerializer.Deserialize<Dictionary<string, string>>(r.PayloadJson) ?? [],
            ReceivedAt = r.ReceivedAt
        }).ToList();
    }
}
