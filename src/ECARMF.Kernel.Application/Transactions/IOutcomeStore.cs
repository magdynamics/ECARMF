using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>Append-only persistence port for transaction outcomes.</summary>
public interface IOutcomeStore
{
    Task AppendAsync(TransactionOutcome outcome, CancellationToken ct = default);
}
