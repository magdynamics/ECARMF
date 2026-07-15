using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Application.Transactions;

public sealed record TransactionSubmission(
    string TenantId,
    string TransactionType,
    string SubmittedBy,
    IReadOnlyDictionary<string, string> Payload,
    /// <summary>Overrides the received timestamp. For backfills and demo data
    /// that must span past periods; live intake leaves it null (= now).</summary>
    DateTimeOffset? OccurredAt = null);

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
/// integrity first); only then is TransactionReceived published. Everything
/// is scoped to the submitting tenant.
/// </summary>
public class TransactionIntakeService : ITransactionIntakeService
{
    private readonly ITransactionStore _store;
    private readonly IKernelEventBus _bus;
    private readonly ITenantRegistryProvider _registries;
    private readonly IAuditLog _audit;

    public TransactionIntakeService(
        ITransactionStore store,
        IKernelEventBus bus,
        ITenantRegistryProvider registries,
        IAuditLog audit)
    {
        _store = store;
        _bus = bus;
        _registries = registries;
        _audit = audit;
    }

    public async Task<TransactionReceipt> ReceiveAsync(TransactionSubmission submission, CancellationToken ct = default)
    {
        var transaction = new Transaction
        {
            TenantId = submission.TenantId,
            EntityType = nameof(Transaction),
            EntityName = submission.TransactionType,
            Status = "Received",
            Owner = submission.SubmittedBy,
            Version = "1",
            TransactionType = submission.TransactionType,
            SubmittedBy = submission.SubmittedBy,
            Payload = new Dictionary<string, string>(submission.Payload),
            ReceivedAt = submission.OccurredAt ?? DateTimeOffset.UtcNow
        };

        await _store.AppendAsync(transaction, ct);

        var receivedDetail = new Dictionary<string, string>(transaction.Payload)
        {
            ["transactionType"] = transaction.TransactionType,
            ["submittedBy"] = transaction.SubmittedBy
        };

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = submission.TenantId,
            CorrelationId = transaction.TransactionId,
            Category = AuditCategories.RecordReceived,
            Actor = transaction.SubmittedBy,
            Summary = $"Record '{transaction.TransactionType}' received from '{transaction.SubmittedBy}'.",
            Detail = receivedDetail,
            OccurredAt = transaction.ReceivedAt
        }, ct);

        var events = _registries.GetFor(submission.TenantId).Events;
        if (!events.IsDeclared(KernelEventNames.RecordReceived))
        {
            return new TransactionReceipt(transaction.TransactionId, transaction.ReceivedAt, false,
                $"Record persisted, but no active Knowledge Package of this tenant declares '{KernelEventNames.RecordReceived}'; no processing will occur.");
        }

        // Rules see the payload fields plus the intake facts. recordType is
        // the canonical key; transactionType is kept as a legacy alias.
        var eventPayload = new Dictionary<string, string>(transaction.Payload, StringComparer.OrdinalIgnoreCase);
        eventPayload.TryAdd("recordType", transaction.TransactionType);
        eventPayload.TryAdd("transactionType", transaction.TransactionType);
        eventPayload.TryAdd("submittedBy", transaction.SubmittedBy);

        await _bus.PublishAsync(new KernelEvent(
            submission.TenantId,
            KernelEventNames.RecordReceived,
            transaction.TransactionId,
            eventPayload,
            DateTimeOffset.UtcNow), ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = submission.TenantId,
            CorrelationId = transaction.TransactionId,
            Category = AuditCategories.EventPublished,
            Actor = transaction.SubmittedBy,
            Summary = $"Event '{KernelEventNames.RecordReceived}' published.",
            Detail = new Dictionary<string, string> { ["eventName"] = KernelEventNames.RecordReceived }
        }, ct);

        return new TransactionReceipt(transaction.TransactionId, transaction.ReceivedAt, true, null);
    }
}
