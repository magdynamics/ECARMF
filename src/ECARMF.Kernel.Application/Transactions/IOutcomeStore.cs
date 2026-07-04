using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>Append-only persistence port for transaction outcomes.
/// Reads are tenant-scoped.</summary>
public interface IOutcomeStore
{
    Task AppendAsync(TransactionOutcome outcome, CancellationToken ct = default);

    /// <summary>Outcomes recorded for the given transactions of a tenant.</summary>
    Task<IReadOnlyList<TransactionOutcome>> GetForTransactionsAsync(
        string tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default);
}
