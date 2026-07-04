using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

public sealed record TransactionSubmission(
    string TransactionType,
    string SubmittedBy,
    IReadOnlyDictionary<string, string> Payload);

public sealed record TransactionReceipt(
    Guid TransactionId,
    DateTimeOffset ReceivedAt,
    bool EventPublished,
    string? Note);

public interface ITransactionIntakeService
{
    Task<TransactionReceipt> ReceiveAsync(TransactionSubmission submission, CancellationToken ct = default);
}

/// <summary>
/// Transaction intake. Order is non-negotiable: the transaction is durably
/// persisted before anything downstream can observe it (audit integrity
/// first); only then is TransactionReceived published.
/// </summary>
public class TransactionIntakeService : ITransactionIntakeService
{
    private readonly ITransactionStore _store;
    private readonly IKernelEventBus _bus;
    private readonly IEventRegistry _events;

    public TransactionIntakeService(ITransactionStore store, IKernelEventBus bus, IEventRegistry events)
    {
        _store = store;
        _bus = bus;
        _events = events;
    }

    public async Task<TransactionReceipt> ReceiveAsync(TransactionSubmission submission, CancellationToken ct = default)
    {
        var transaction = new Transaction
        {
            EntityType = nameof(Transaction),
            EntityName = submission.TransactionType,
            Status = "Received",
            Owner = submission.SubmittedBy,
            Version = "1",
            TransactionType = submission.TransactionType,
            SubmittedBy = submission.SubmittedBy,
            Payload = new Dictionary<string, string>(submission.Payload),
            ReceivedAt = DateTimeOffset.UtcNow
        };

        await _store.AppendAsync(transaction, ct);

        if (!_events.IsDeclared(KernelEventNames.TransactionReceived))
        {
            return new TransactionReceipt(transaction.TransactionId, transaction.ReceivedAt, false,
                $"Transaction persisted, but no active Knowledge Package declares '{KernelEventNames.TransactionReceived}'; no processing will occur.");
        }

        // Rules see the payload fields plus the intake facts.
        var eventPayload = new Dictionary<string, string>(transaction.Payload, StringComparer.OrdinalIgnoreCase);
        eventPayload.TryAdd("transactionType", transaction.TransactionType);
        eventPayload.TryAdd("submittedBy", transaction.SubmittedBy);

        await _bus.PublishAsync(new KernelEvent(
            KernelEventNames.TransactionReceived,
            transaction.TransactionId,
            eventPayload,
            DateTimeOffset.UtcNow), ct);

        return new TransactionReceipt(transaction.TransactionId, transaction.ReceivedAt, true, null);
    }
}
