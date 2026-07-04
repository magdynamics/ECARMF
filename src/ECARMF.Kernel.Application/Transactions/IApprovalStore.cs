using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

/// <summary>Append-only persistence port for dual-approval decisions.</summary>
public interface IApprovalStore
{
    Task AppendAsync(ApprovalDecision decision, CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalDecision>> GetForTransactionAsync(
        string tenantId, Guid transactionId, CancellationToken ct = default);
}
