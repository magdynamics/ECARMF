using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>
/// Append-only persistence port for transactions. No update or delete methods
/// exist by design: immutability of received transactions is structural.
/// </summary>
public interface ITransactionStore
{
    Task AppendAsync(Transaction transaction, CancellationToken ct = default);
}
