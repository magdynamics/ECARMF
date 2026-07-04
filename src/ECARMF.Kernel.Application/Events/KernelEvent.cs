namespace ECARMF.Kernel.Application.Events;

/// <summary>An event flowing through the kernel bus. CorrelationId ties the
/// event (and everything it causes) back to the originating transaction.</summary>
public sealed record KernelEvent(
    string EventName,
    Guid CorrelationId,
    IReadOnlyDictionary<string, string> Payload,
    DateTimeOffset OccurredAt);
