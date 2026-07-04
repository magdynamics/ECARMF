using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
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
/// persisted and audited before anything downstream can observe it (audit
/// integrity first); only then is TransactionReceived published.
/// </summary>
public class TransactionIntakeService : ITransactionIntakeService
{
    private readonly ITransactionStore _store;
    private readonly IKernelEventBus _bus;
    private readonly IEventRegistry _events;
    private readonly IAuditLog _audit;

    public TransactionIntakeService(
        ITransactionStore store,
        IKernelEventBus bus,
        IEventRegistry events,
        IAuditLog audit)
    {
        _store = store;
        _bus = bus;
        _events = events;
        _audit = audit;
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

        var receivedDetail = new Dictionary<string, string>(transaction.Payload)
        {
            ["transactionType"] = transaction.TransactionType,
            ["submittedBy"] = transaction.SubmittedBy
        };

        await _audit.AppendAsync(new AuditEntry
        {
            CorrelationId = transaction.TransactionId,
            Category = AuditCategories.TransactionReceived,
            Summary = $"Transaction '{transaction.TransactionType}' received from '{transaction.SubmittedBy}'.",
            Detail = receivedDetail,
            OccurredAt = transaction.ReceivedAt
        }, ct);

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

        await _audit.AppendAsync(new AuditEntry
        {
            CorrelationId = transaction.TransactionId,
            Category = AuditCategories.EventPublished,
            Summary = $"Event '{KernelEventNames.TransactionReceived}' published.",
            Detail = new Dictionary<string, string> { ["eventName"] = KernelEventNames.TransactionReceived }
        }, ct);

        return new TransactionReceipt(transaction.TransactionId, transaction.ReceivedAt, true, null);
    }
}
