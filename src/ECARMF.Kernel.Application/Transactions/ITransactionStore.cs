using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>
/// Persistence port for transactions. Writes are append-only: no update or
/// delete methods exist by design, so immutability of received transactions
/// is structural. Reads are tenant-scoped.
/// </summary>
public interface ITransactionStore
{
    Task AppendAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>Most recent transactions for a tenant, newest first.</summary>
    Task<IReadOnlyList<Transaction>> GetRecentAsync(string tenantId, int limit, CancellationToken ct = default);

    Task<Transaction?> GetByIdAsync(string tenantId, Guid transactionId, CancellationToken ct = default);
}
