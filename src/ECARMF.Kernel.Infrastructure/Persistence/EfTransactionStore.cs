using System.Text.Json;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Transactions;

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
            TransactionType = transaction.TransactionType,
            SubmittedBy = transaction.SubmittedBy,
            PayloadJson = JsonSerializer.Serialize(transaction.Payload),
            ReceivedAt = transaction.ReceivedAt
        });

        await _db.SaveChangesAsync(ct);
    }
}
